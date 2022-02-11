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
using Newtonsoft.Json.Linq;

namespace Company.Function
{
    public static class HttpTrigger1
    {
        static XslCompiledTransform v2toV4xsl, v4cdsltoopenapixsl, csdltoodataversion; 

        [FunctionName("HttpTrigger1")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req, ILogger log)
        {
            //ToDo: determine runtime xsl location depending on environment
            //string root = $"{System.Environment.GetEnvironmentVariable("HOME")}{Path.DirectorySeparatorChar}";
            string root = $"{System.Environment.GetEnvironmentVariable("HOME")}{Path.DirectorySeparatorChar}site{Path.DirectorySeparatorChar}wwwroot{Path.DirectorySeparatorChar}";

            try
            {
                log.LogInformation("C# HTTP trigger function processed a request.");
            
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
                //string versionod = ApplyTransform(v4OdataXml,csdltoodataversion);

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
                
                if (bool.Parse(req.Headers["x-openapi-enrich-tags"]))
                {
                    // Monkey Patch the Missing Description for Power Platform
      
                    JArray tags = (JArray)openapi["tags"];
                    foreach (JObject item in tags)
                    {
                        item.Property("name").AddAfterSelf(new JProperty("description", item.GetValue("name").ToString()));
                    }
                    
                }
                // Monkey Patch the Weird decimal scenario to assist with OData to OpenAPI representation
                // (OpenAPI's types are based on JSON Schema which has no concept of a 'decimal' data type)
                // and Power Platform has an issue with the converter's output as there is an EDM:Decimal in the OData spec.

                // OpenAPI3.0
                // components.schemas['GWSAMPLE_BASIC.Product'].properties.WeightMeasure.anyOf[0]"

                // OpenAPI2.0 (Swagger)
                // definitions['GWSAMPLE_BASIC.Product'].properties.WeightMeasure.anyOf[0]"

                // JToken acme = openapi.SelectToken("$.definitions[?(@.properties == 'Acme Co')]");

                // foreach (JProperty jdefinition in openapi["definitions"])
                // {
                //     At this point we are looping through each object definition
                //     foreach (JProperty jpropertyobject in jdefinition.Value)
                //     {
                //         Now we are looking for a child property object
                //         if(jpropertyobject.Name == "properties")
                //         {
                //             log.LogInformation($"3.--->Property {jdefinition.Name} | {jpropertyobject.Name}");

                //             foreach (var propertyfieldinner2 in jpropertyobject.Value)
                //             {
                //                 Now we are looking through the fields of that object 
                               
                //                 foreach (var propertyfieldinner3 in propertyfieldinner2)
                //                 {
                //                     now parse the type and format of that object
                //                     if(
                //                         propertyfieldinner3["type"].Contains("string") 
                //                         && propertyfieldinner3["type"].Contains("string") 
                //                         && propertyfieldinner3["format"].ToString() == "decimal"
                //                     )
                //                     {
                //                         log.LogInformation($"4. -Found One ! {jdefinition.Name} | {jpropertyobject.Name} | {propertyfieldinner2} | {propertyfieldinner3}");
                //                     }
                //                 }
                //             }
                //         }
                //     }
                // }
               
                string transformfinal = openapi.ToString();

                return new OkObjectResult(transformfinal);

            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult($"An Error Occurred handling your request. /r/n{ex.Source}: {ex.Message} /r/n Running from {root}");
            }
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

     // Tags property object
     // Root -> 
     // Tags array of objects name is present, 
     // but description is not


    }
}
