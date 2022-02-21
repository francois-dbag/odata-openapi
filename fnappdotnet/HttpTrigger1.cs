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
        static XslCompiledTransform v2toV4xsl, v4cdsltoopenapixsl, csdltoodataversion; 
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
                 root = $"{System.Environment.GetEnvironmentVariable("HOME")}{Path.DirectorySeparatorChar}site{Path.DirectorySeparatorChar}wwwroot{Path.DirectorySeparatorChar}";
            }
            try
            {
                log.LogInformation("C# HTTP trigger function processed a request to convert OData to OpenAPI");
                if (v2toV4xsl == null)
                {
                    log.LogInformation("First run on this host - caching stylesheets and transforms from " + root);
                    v2toV4xsl = new XslCompiledTransform();
                    v2toV4xsl.Load(root + "V2-to-V4-CSDL.xsl");
                    v4cdsltoopenapixsl = new XslCompiledTransform();
                    v4cdsltoopenapixsl.Load(root + "V4-CSDL-to-OpenAPI.xsl"); 
                    csdltoodataversion = new XslCompiledTransform();
                    csdltoodataversion.Load(root + "OData-Version.xsl"); 
                    log.LogInformation("First run completed, transforms loaded and compiled.");
                }
                log.LogInformation("Converting to v4");
                // Convert it to V4 OData First, then convert to OData, then return
                string v4OdataXml = ApplyTransform(await req.ReadAsStringAsync(), v2toV4xsl);
                // string versionod = ApplyTransform(v4OdataXml,csdltoodataversion);
                var args = new XsltArgumentList() ;
                args.AddParam("scheme","", req.Headers["x-scheme"].FirstOrDefault() ?? "https");
                args.AddParam("host","",req.Headers["x-host"].FirstOrDefault() ?? "services.odata.org");
                args.AddParam("basePath","",req.Headers["x-basepath"].FirstOrDefault() ?? "/service-root");
                args.AddParam("odata-version","","2.0.0");
                args.AddParam("diagram","","YES");
                args.AddParam("openapi-root","", "https://raw.githubusercontent.com/oasis-tcs/odata-openapi/master/examples/");     
                args.AddParam("openapi-version","" , req.Headers["x-openapi-version"].FirstOrDefault() ?? "3.0.0");   
                string transformoutput = ApplyTransform(v4OdataXml, v4cdsltoopenapixsl, args);
                JObject openapi = JObject.Parse(transformoutput);
                if (bool.Parse(req.Headers["x-openapi-enrich-tags"].FirstOrDefault() ?? "false"))
                {
                    // Monkey Patch the Missing Description for Power Platform
                    JArray tags = (JArray)openapi["tags"];
                    foreach (JObject item in tags)
                    {
                        item.Property("name").AddAfterSelf(new JProperty("description", item.GetValue("name").ToString()));
                    }
                }
                if (bool.Parse(req.Headers["x-openapi-enrich-decimals"].FirstOrDefault() ?? "false"))
                {
                    // Monkey Patch the Weird decimal scenario to assist with OData to OpenAPI representation
                    // (OpenAPI's types are based on JSON Schema which has no concept of a 'decimal' data type)
                    // and Power Platform has an issue with the converter's output IF there is an EDM:Decimal in the OData spec.
                    JToken jtroot = null; 
                    var PatchList = new List<PatchData>(); 
                    // Info/Description parse and truncate to 1000 characters
                    openapi["info"]["description"] = new JValue(openapi["info"]["description"].Value<string>().Substring(0,1000));
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
                        jtroot = openapi["definitions"];                     
                        foreach (JProperty jdefinition in jtroot)
                        {
                            // At this point we are looping through each object definition (So in each of definitions['x'])
                            foreach (JProperty jpropertyobject in jdefinition.Value)
                            {
                                if(jpropertyobject.Name == "properties") // Now we are looking for a child property object (So definitions['x'].properties)
                                {
                                    foreach (JProperty propertyfieldinner2 in jpropertyobject.Value)
                                    {
                                        foreach (var propertyfieldinner3 in propertyfieldinner2) // Now we are looking through the fields of that object 
                                        {
                                            // Now parse the type and format of that object
                                            bool isString = propertyfieldinner3["type"]?.ToString() == "string"; 
                                            bool hasDecimal = propertyfieldinner3["format"]?.ToString() == "decimal"; 
                                            bool isArrayAndHasString = false;
                                            bool isNullable = false;
                                            // If the type property is an array then parse the array values to check for a string value
                                            // IsString will be true if there is only one property called 'string'.
                                            // But here we look for that value as an element in the array
                                            if(!isString && (propertyfieldinner3["type"]?.Count() > 1))
                                            {
                                                foreach (var prop in propertyfieldinner3["type"])
                                                {
                                                    if(prop.ToString() == "null")
                                                    {
                                                        isNullable = true;
                                                    }
                                                    if(prop.ToString() == "string") 
                                                    {
                                                        isArrayAndHasString = true;
                                                    }
                                                }
                                            }
                                            // Detection complete, time to monkey-patch the json and replace the string type with a float type
                                            if (hasDecimal)
                                            {
                                                PatchList.Add(new PatchData() {
                                                    Path = propertyfieldinner3["type"].Path, 
                                                    IsString = isString,
                                                    IsArrayAndHasString = isArrayAndHasString,
                                                    IsNullable = isNullable
                                                });
                                                log.LogInformation($"{propertyfieldinner3["type"].Path}, IsString: {isString}, IsArrayAndHasString: {isArrayAndHasString}, IsNullable: isNullable");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        // We need to do the actual patching here. 
                        foreach (PatchData patchme in PatchList)
                        {
                            if (openapi.SelectToken(patchme.Path, true).Count() > 1)
                            {
                                JArray tmparray = new JArray(); 
                                foreach (JValue jv in (JArray) openapi.SelectToken(patchme.Path, true))
                                {
                                    if (jv.ToString() != "integer" && jv.ToString() != "null") tmparray.Add(jv);
                                }
                                // tmparray.Add(new JValue(patchToWhat));
                                openapi.SelectToken(patchme.Path, true).Replace(tmparray);
                            }
                            else
                            {
                                openapi.SelectToken(patchme.Path, true)["type"].Remove();
                                var old = (JProperty) openapi.SelectToken(patchme.Path, true);
                                old.Add(new JProperty("type", new JValue(patchToWhat))); 
                            }
                        }
                    }
                    if ((req.Headers["x-openapi-version"].FirstOrDefault() ?? "3.0.0").StartsWith("3.0"))
                    {
                        // OpenAPI3.0
                        // WeightMeasure": {
                        // "anyOf": [
                        //     {
                        //         "type": "number"
                        //     },
                        //     {
                        //         "type": "string"
                        //     }
                        // ],
                        // "nullable": true,
                        // "format": "decimal",
                        jtroot = openapi["components"]["schemas"]; 
                        foreach (JProperty jdefinition in jtroot)
                        {
                            // At this point we are looping through each object definition (So in each of definitions['x'])
                            foreach (JProperty jpropertyobject in jdefinition.Value)
                            {
                                if(jpropertyobject.Name == "properties") // Now we are looking for a child property object (So definitions['x'].properties)
                                {
                                    foreach (JProperty propertyfieldinner2 in jpropertyobject.Value)
                                    {
                                        foreach (var propertyfieldinner3 in propertyfieldinner2) // Now we are looking through the fields of that object 
                                        {
                                            // Is it a reference to another object ? 
                                            if(propertyfieldinner3.Children().Count() > 1)
                                            {
                                                // Now parse the type and format of that object
                                                bool hasDecimal = propertyfieldinner3["format"]?.ToString() == "decimal"; 
                                                bool isArrayAndHasString = false;
                                                JArray typesanyof = (JArray) propertyfieldinner3["anyOf"] ?? new JArray(); 
                                                foreach (JObject typedescr in typesanyof)
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
                                                        Path = propertyfieldinner3["anyOf"].Path, 
                                                        IsArrayAndHasString = isArrayAndHasString,
                                                    });
                                                    log.LogInformation($"{propertyfieldinner3["anyOf"].Path}, IsArrayAndHasString: {isArrayAndHasString}");
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        
                        // We need to do the actual patching here - Remove the array anyOf, then remove the multipleOf property, then add the singular type property.
                        foreach (PatchData patchme in PatchList)
                        {
                            ((JArray)openapi.SelectToken(patchme.Path, true)).Parent.Remove();
                            ((JValue)openapi.SelectToken(patchme.Path.Replace("anyOf","multipleOf"), true)).Parent.Remove();
                            ((JObject)openapi.SelectToken(patchme.Path.Replace(".anyOf",""), true)).Add(new JProperty("type", new JValue(patchToWhat)));
                        }

                        if (bool.Parse(req.Headers["x-openapi-enrich-ifmatchtag"].FirstOrDefault() ?? "false"))
                        {
                            // var operationlist = new List<PatchData>(); 
                            // Find the PATCH and DELETE operations and monkeypatch in the if-match header to hold the etag
                            foreach (JProperty jpath in openapi["paths"])
                            {
                                foreach (JObject jrestmethod in jpath)    // At this point we are looping through each each rest method
                                {
                                    ProcessMe processme = ProcessMe.dontprocess; 
                                    if (jrestmethod["patch"]  != null) processme |= ProcessMe.patch;
                                    if (jrestmethod["delete"] != null) processme |= ProcessMe.delete;
                                    if (jrestmethod["get"]    != null) processme |= ProcessMe.get;
                                    if (processme.HasFlag(ProcessMe.patch))
                                    {
                                        ProcessPatchOrDelete(jrestmethod, "patch");
                                    }
                                    if (processme.HasFlag(ProcessMe.delete))
                                    {
                                        ProcessPatchOrDelete(jrestmethod, "delete");
                                    }
                                }
                            }
                        }
                    }
                }
                string transformfinal = openapi.ToString();
                return new OkObjectResult(transformfinal);
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult($"An Error Occurred handling your request. \r\n{ex.Source} \r\n\r\n{ex.Message} \r\n\r\n Running from {root}");
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
