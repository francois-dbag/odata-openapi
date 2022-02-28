using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Xml.Xsl;
using System.Xml;
using System.Linq;
using System;
using Newtonsoft.Json; 
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
namespace Company.Function
{
    public static class HttpTrigger1
    {
        static XslCompiledTransform v2toV4xsl, v4CSDLToOpenAPIXslt, CSDLToODataVersion; 
        [FunctionName("HttpTrigger1")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req, ILogger log)
        {
            string root = ""; 
            string patchToWhat = System.Environment.GetEnvironmentVariable("PatchToWhat") ?? "number";

            if (bool.Parse(System.Environment.GetEnvironmentVariable("OverrideLocalTransformFilesInRoot") ?? "false"))
            {
                root = $"{System.Environment.GetEnvironmentVariable("HOME")}{Path.DirectorySeparatorChar}";
            }
            else
            {
                root = Path.Combine(System.Environment.GetEnvironmentVariable("HOME"), "site", "wwwroot");
            }
            try
            {

                log.LogInformation("C# HTTP trigger function processed a request to convert OData to OpenAPI");
                if (v2toV4xsl == null)
                {
                    log.LogInformation("First run on this host - caching stylesheets and transforms from " + root);
                    v2toV4xsl = new XslCompiledTransform();
                    v2toV4xsl.Load(root + "V2-to-V4-CSDL.xsl");
                    v4CSDLToOpenAPIXslt = new XslCompiledTransform();
                    v4CSDLToOpenAPIXslt.Load(root + "V4-CSDL-to-OpenAPI.xsl"); 
                    CSDLToODataVersion = new XslCompiledTransform();
                    CSDLToODataVersion.Load(root + "OData-Version.xsl"); 
                    log.LogInformation("First run completed, transforms loaded and compiled.");
                }

                log.LogInformation("Converting to v4");

                // Convert it to V4 OData First, then convert to OpenAPI, then return
                string v4OdataXml = ApplyTransform(await req.ReadAsStringAsync(), v2toV4xsl);
                var args = new XsltArgumentList() ;

                args.AddParam("scheme","", (req.Headers["x-scheme"].FirstOrDefault() ?? "") == "" ? "https" : req.Headers["x-scheme"].FirstOrDefault());
                args.AddParam("host","", (req.Headers["x-host"].FirstOrDefault() ?? "") == "" ? "services.odata.org" : req.Headers["x-host"].FirstOrDefault());
                args.AddParam("basePath","", (req.Headers["x-basepath"].FirstOrDefault() ?? "") == "" ? "/service-root" : req.Headers["x-basepath"].FirstOrDefault());
                args.AddParam("odata-version","","2.0"); // Could use ApplyTransform(v4OdataXml, CSDLToODataVersion) here if we cared about versions other than v2.0
                args.AddParam("diagram","","YES");
                args.AddParam("openapi-root","", "https://raw.githubusercontent.com/oasis-tcs/odata-openapi/master/examples/");     
                args.AddParam("openapi-version","" , req.Headers["x-openapi-version"].FirstOrDefault() ?? "3.0.0"); 

                string transformoutput = ApplyTransform(v4OdataXml, v4CSDLToOpenAPIXslt, args);
                JObject openapi = JObject.Parse(transformoutput);
                
                var PatchList = new List<PatchData>(); 

                if (bool.Parse(req.Headers["x-openapi-truncate-description"].FirstOrDefault() ?? "false"))
                {
                    // Info/Description parse and truncate to remove the EDM 
                    openapi["info"]["description"] = new JValue(openapi["info"]["description"].Value<string>().Substring(0,openapi["info"]["description"].Value<string>().IndexOf("##")));//.Replace("\n"," ").Replace("\r"," "));
                }

                if (bool.Parse(req.Headers["x-openapi-enrich-tags"].FirstOrDefault() ?? "false"))
                {
                    // Monkey Patch the Missing Description for Power Platform
                    JArray tags = (JArray)openapi["tags"];
                    foreach (JObject item in tags)
                    {
                        item.Property("name").AddAfterSelf(new JProperty("description", item.GetValue("name").ToString()));
                    }
                }
               
                if (bool.Parse(req.Headers["x-openapi-add-metadata-etags"].FirstOrDefault() ?? "false"))
                {
                    if ((req.Headers["x-openapi-version"].FirstOrDefault() ?? "3.0.0").StartsWith("3.0"))
                    {
                        foreach (JProperty JSchemaDefinitions in openapi["components"]["schemas"])
                        {
                            foreach (JProperty JPropertyObject in JSchemaDefinitions.Value)
                            {
                                if(JPropertyObject.Name == "properties") // Now we are looking for a child property object (So components/schemas['x'].properties)
                                {
                                    ((JObject)JPropertyObject.Value).AddFirst(
                                        new JProperty("__metadata", 
                                            new JObject(
                                                new JProperty("type", "object"), 
                                                new JProperty("properties", 
                                                    new JObject(
                                                        new JProperty(
                                                            "etag", 
                                                            new JObject(
                                                                new JProperty("nullable", true), 
                                                                new JProperty("type","string")
                                                            )
                                                        )
                                                    )
                                                )
                                            )
                                        )
                                    );
                                }
                            }
                        }
                    }
                    else
                    {

                        // ToDo: Fix Unable to cast object of type 'Newtonsoft.Json.Linq.JArray' to type 'Newtonsoft.Json.Linq.JValue'. error
                        foreach (JProperty JSchemaDefinitions in openapi["definitions"])
                        {
                            foreach (JProperty JPropertyObject in JSchemaDefinitions.Value)
                            {
                                if(JPropertyObject.Name == "properties") // Now we are looking for a child property object (So components/schemas['x'].properties)
                                {
                                    ((JObject)JPropertyObject.Value).AddFirst(
                                        new JProperty("__metadata", 
                                            new JObject(
                                                new JProperty("type", "object"), 
                                                new JProperty("properties", 
                                                    new JObject(
                                                        new JProperty(
                                                            "etag", 
                                                            new JObject(
                                                                new JProperty("nullable", true), 
                                                                new JProperty("type","string")
                                                            )
                                                        )
                                                    )
                                                )
                                            )
                                        )
                                    );
                                }
                            }
                        }
                    }
                }

                if (bool.Parse(req.Headers["x-openapi-ifmatch"].FirstOrDefault() ?? "false"))
                {
                    // var operationlist = new List<PatchData>(); 
                    // Find the PATCH and DELETE operations and monkeypatch in the if-match header to hold the etag
                    foreach (JProperty JPropertyPath in openapi["paths"])
                    {
                        foreach (JObject JObjectRESTMethod in JPropertyPath)    // At this point we are looping through each each rest method
                        {
                            ProcessMe processme = ProcessMe.dontprocess; 
                            if (JObjectRESTMethod["patch"]  != null) processme |= ProcessMe.patch;
                            if (JObjectRESTMethod["delete"] != null) processme |= ProcessMe.delete;
                            if (JObjectRESTMethod["get"]    != null) processme |= ProcessMe.get;
                            if (JObjectRESTMethod["put"]    != null) processme |= ProcessMe.put;
                            if (JObjectRESTMethod["post"]   != null) processme |= ProcessMe.post;
                            if (processme.HasFlag(ProcessMe.patch))
                            {
                                ProcessPatchOrDelete(JObjectRESTMethod, "patch");
                            }
                            if (processme.HasFlag(ProcessMe.delete))
                            {
                                ProcessPatchOrDelete(JObjectRESTMethod, "delete");
                            }
                        }
                    }
                }

                if (bool.Parse(req.Headers["x-openapi-enrich-decimals"].FirstOrDefault() ?? "false"))
                {
                    // Monkey Patch the Weird decimal scenario to assist with OData to OpenAPI representation
                    // remove the multipleOf representation so powerplatform can import it. 
                    if ((req.Headers["x-openapi-version"].FirstOrDefault() ?? "3.0.0").StartsWith("2.0"))
                    {
                        // OpenAPI2.0 (Swagger)
                        // "WeightMeasure": {   // Looking for a string in the 'type' property and a format 'decimal' value
                        //          "type": [
                        //             "number",
                        //             "string", 
                        //             "null"
                        //              ],
                        //          "format": "decimal"
                        //         },

                        var JTRoot = openapi["definitions"];                     
                        foreach (JProperty JDefinition in JTRoot)
                        {
                            // At this point we are looping through each object definition (So in each of definitions['x'])
                            foreach (JProperty JPropertyObject in JDefinition.Value)
                            {
                                if(JPropertyObject.Name == "properties") // Now we are looking for a child property object (So definitions['x'].properties)
                                {
                                    foreach (JProperty JPropertyFieldInnerProperty in JPropertyObject.Value)
                                    {
                                        foreach (var JPropertyFieldInnerPropertyProperty in JPropertyFieldInnerProperty) // Now we are looking through the fields of that object 
                                        {
                                            // Now parse the type and format of that object
                                            bool isString = JPropertyFieldInnerPropertyProperty["type"]?.ToString() == "string"; 
                                            bool hasDecimal = JPropertyFieldInnerPropertyProperty["format"]?.ToString() == "decimal"; 
                                            bool isArrayAndHasString = false;
                                            bool isNullable = false;
                                            // If the type property is an array then parse the array values to check for a string value
                                            // IsString will be true if there is only one property called 'string'.
                                            // But here we look for that value as an element in the array
                                            if(!isString && (JPropertyFieldInnerPropertyProperty["type"]?.Count() > 1))
                                            {
                                                foreach (var JPropertyType in JPropertyFieldInnerPropertyProperty["type"])
                                                {
                                                    if(JPropertyType.ToString() == "null")
                                                    {
                                                        isNullable = true;
                                                    }
                                                    if(JPropertyType.ToString() == "string") 
                                                    {
                                                        isArrayAndHasString = true;
                                                    }
                                                }
                                            }
                                            // Detection complete, time to monkey-patch the json and replace the string type with a float type
                                            if (hasDecimal)
                                            {
                                                PatchList.Add(new PatchData() {
                                                    Path = JPropertyFieldInnerPropertyProperty["type"].Path, 
                                                    IsString = isString,
                                                    IsArrayAndHasString = isArrayAndHasString,
                                                    IsNullable = isNullable
                                                });
                                                log.LogInformation($"{JPropertyFieldInnerPropertyProperty["type"].Path}, IsString: {isString}, IsArrayAndHasString: {isArrayAndHasString}, IsNullable: isNullable");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        // We need to do the actual patching here. 
                        foreach (PatchData PatchItem in PatchList)
                        {
                            if (openapi.SelectToken(PatchItem.Path, true).Count() > 1)
                            {
                                JArray JArrayTypes = new JArray(); 
                                foreach (JValue jv in (JArray) openapi.SelectToken(PatchItem.Path, true))
                                {
                                    if (jv.ToString() != "integer" && jv.ToString() != "null") JArrayTypes.Add(jv);
                                }
                                openapi.SelectToken(PatchItem.Path, true).Replace(JArrayTypes);
                            }
                            else
                            {
                                openapi.SelectToken(PatchItem.Path, true)["type"].Remove();
                                var old = (JProperty) openapi.SelectToken(PatchItem.Path, true);
                                old.Add(new JProperty("type", new JValue(patchToWhat))); 
                            }

                            ((JValue)openapi.SelectToken(PatchItem.Path.Replace("anyOf","multipleOf"), true)).Parent.Remove();
                        }
                    }

                    if ((req.Headers["x-openapi-version"].FirstOrDefault() ?? "3.0.0").StartsWith("3.0"))
                    {
                        var JTokenRoot = openapi["components"]["schemas"]; 
                        foreach (JProperty JSchemaDefinition in JTokenRoot)
                        {
                            // At this point we are looping through each object definition (So in each of definitions['x'])
                            foreach (JProperty JPropertyObject in JSchemaDefinition.Value)
                            {
                                if(JPropertyObject.Name == "properties") // Now we are looking for a child property object (So definitions['x'].properties)
                                {
                                    foreach (JProperty JPropertyFieldInnerProperty in JPropertyObject.Value)
                                    {
                                        foreach (var JPropertyFieldInnerPropertyProperty in JPropertyFieldInnerProperty) // Now we are looking through the fields of that object 
                                        {
                                            // Is it a reference to another object ? 
                                            if(JPropertyFieldInnerPropertyProperty.Children().Count() > 1)
                                            {
                                                // Now parse the type and format of that object
                                                bool hasDecimal = JPropertyFieldInnerPropertyProperty["format"]?.ToString() == "decimal"; 
                                                bool isArrayAndHasString = false;

                                                JArray JArrayAnyOf = (JArray) JPropertyFieldInnerPropertyProperty["anyOf"] ?? new JArray(); 
                                                foreach (JObject typedescr in JArrayAnyOf)
                                                {
                                                    if (typedescr.GetValue("type").ToString() == "string") 
                                                    {
                                                             isArrayAndHasString = true;
                                                    }
                                                }
                                                // Detection complete, time to monkey-patch the json and replace the string type with a float type
                                                if (hasDecimal)
                                                {
                                                    PatchList.Add(new PatchData() {
                                                        Path = JPropertyFieldInnerPropertyProperty["anyOf"]?.Path ?? JPropertyFieldInnerPropertyProperty.Path, 
                                                        IsArrayAndHasString = isArrayAndHasString,
                                                    });
                                                    log.LogInformation($"{JPropertyFieldInnerPropertyProperty["anyOf"]?.Path ?? JPropertyFieldInnerPropertyProperty.Path}, IsArrayAndHasString: {isArrayAndHasString}");
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // We need to do the actual patching here so if just a string type, remove the multipleOf property only.
                        // If this is an array, then remove the array anyOf, then remove the multipleOf property, then add the singular type property.
                        foreach (PatchData PatchMe in PatchList)
                        {
                            if(PatchMe.IsArrayAndHasString) ((JArray)openapi.SelectToken(PatchMe.Path, true)).Parent.Remove();
                            if(PatchMe.IsArrayAndHasString) 
                            {
                                ((JValue)openapi.SelectToken(PatchMe.Path.Replace("anyOf","multipleOf"), true)).Remove();
                            }
                            else
                            {
                                openapi.SelectToken($"{PatchMe.Path}['multipleOf']", true).Parent.Remove();
                            }
                            if(PatchMe.IsArrayAndHasString) ((JObject)openapi.SelectToken(PatchMe.Path.Replace(".anyOf",""), true)).Add(new JProperty("type", new JValue(patchToWhat)));
                        } 
                    }
                }
                
                string transformfinal = openapi.ToString();
                return new OkObjectResult(transformfinal);
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult($"An Error Occurred handling your request. \r\n{ex.Source}\r\n{ex.Message}");// \r\n\r\n Running from {root}");
            }
        }

        private static void ProcessPatchOrDelete(JObject jrestmethod, string processme)
        {
            // Check to see if the parameters jarray exists
            JArray jsonparams;
            if (jrestmethod[processme]?["parameters"] != null)
            {
                jsonparams = (JArray)jrestmethod[processme]?["parameters"];
            }
            else
            {
                jsonparams = new JArray();
                ((JObject)jrestmethod[processme]).Add(new JProperty("parameters", jsonparams));
            }
            
            // Add a JObject for the if-match etag check header
            var paramobj = new JObject();
            paramobj.Add("name", "if-match");
            paramobj.Add("in", "header");
            paramobj.Add("required", true);
            paramobj.Add("schema", new JObject(new JProperty("type", "string")));
            paramobj.Add("x-ms-visibility", "important");
            paramobj.Add("x-ms-summary", "Place the eTag value for optimistic concurrency control in this header");

            // Add to the array
            jsonparams.Add(paramobj);

        }

        public static string ApplyTransform(string input, XslCompiledTransform xslTrans, XsltArgumentList args = null)
        {
            using (StringReader str = new StringReader(input))
            {
                using (XmlReader xr = XmlReader.Create(str))
                {
                    using (StringWriter sw = new StringWriter())
                    {
                        xslTrans.Transform(xr, args, sw);
                        return sw.ToString();
                    }
                }
            }
        }
    }

    [Flags()]
    public enum ProcessMe
    {
        dontprocess = 0,
        get = 1,
        post = 2, 
        put = 4,
        delete = 8,
        patch = 16
    }
    public class PatchData
    {
        public string Path { get; set; } 
        public bool IsString { get; set; } 
        public bool IsArrayAndHasString { get; set; } 
        public bool IsNullable { get; set; } 
    }
}
               

                                   