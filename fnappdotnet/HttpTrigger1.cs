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

namespace Company.Function
{
    public static class HttpTrigger1
    {
        static XslCompiledTransform v2toV4xsl, v4cdsltoopenapixsl, csdltoodataversion; 

        [FunctionName("HttpTrigger1")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req, ILogger log)
        {
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

                string v4OdataXml = ApplyTransform(await req.ReadAsStringAsync(),v2toV4xsl);
                //string versionod = ApplyTransform(v4OdataXml,csdltoodataversion);

                var args = new XsltArgumentList() ;
                args.AddParam("scheme","", req.Headers["x-scheme"].FirstOrDefault() ?? "https");
                args.AddParam("host","",req.Headers["x-host"].FirstOrDefault() ?? "services.odata.org");
                args.AddParam("basePath","",req.Headers["x-basepath"].FirstOrDefault() ?? "/service-root");
                args.AddParam("odata-version","","4.0.0");
                args.AddParam("diagram","","YES");
                args.AddParam("openapi-root","", "https://raw.githubusercontent.com/oasis-tcs/odata-openapi/master/examples/");
                args.AddParam("openapi-version","", "3.0.0");           

                return new OkObjectResult(ApplyTransform(v4OdataXml, v4cdsltoopenapixsl, args));
                log.LogInformation(args.ToString()); 
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
    }
}
