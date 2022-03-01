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
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Writers;
using Microsoft.OpenApi.Any;

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
                root = $"{System.Environment.GetEnvironmentVariable("HOME")}";
            }
            else
            {
                root = Path.Combine(System.Environment.GetEnvironmentVariable("HOME"), "site", "wwwroot") ;
            }

            log.LogInformation("C# HTTP trigger function processed a request to convert OData to OpenAPI");
            if (v2toV4xsl == null)
            {
                log.LogInformation("First run on this host - caching stylesheets and transforms from " + root);
                v2toV4xsl = new XslCompiledTransform();
                v2toV4xsl.Load(Path.Combine(root, "V2-to-V4-CSDL.xsl"));
                v4CSDLToOpenAPIXslt = new XslCompiledTransform();
                v4CSDLToOpenAPIXslt.Load(Path.Combine(root, "V4-CSDL-to-OpenAPI.xsl")); 
                CSDLToODataVersion = new XslCompiledTransform();
                CSDLToODataVersion.Load(Path.Combine(root, "OData-Version.xsl")); 
                log.LogInformation("First run completed, transforms loaded and compiled.");
            }

            log.LogInformation("Converting to v4");

            // Convert it to V4 OData First, then convert to OpenAPI, then return
            string v4OdataXml = ApplyTransform(await req.ReadAsStringAsync(), v2toV4xsl);
            var args = new XsltArgumentList() ;

            args.AddParam("scheme","", (req.Headers["x-scheme"].FirstOrDefault() ?? "") == "" ? "https" : req.Headers["x-scheme"].FirstOrDefault());
            args.AddParam("host","", (req.Headers["x-host"].FirstOrDefault() ?? "") == "" ? "services.odata.org" : req.Headers["x-host"].FirstOrDefault());
            args.AddParam("basePath","", (req.Headers["x-basepath"].FirstOrDefault() ?? "") == "" ? "/service-root" : req.Headers["x-basepath"].FirstOrDefault());
            args.AddParam("odata-version","", "2.0"); // ApplyTransform(v4OdataXml, CSDLToODataVersion)); // Could use  here if we cared about versions other than v2.0
            args.AddParam("diagram","","YES");
            args.AddParam("openapi-root","", "https://raw.githubusercontent.com/oasis-tcs/odata-openapi/master/examples/");     
            args.AddParam("openapi-version","" , "3.0.0"); 

            string transformoutput = ApplyTransform(v4OdataXml, v4CSDLToOpenAPIXslt, args);
            JObject openapi = JObject.Parse(transformoutput);

            OpenApiDocument openApiDocument = new OpenApiStringReader().Read(openapi.ToString(), out var diagnostic);

            if (bool.Parse(req.Headers["x-openapi-enrich-decimals"].FirstOrDefault() ?? "false"))
            {
                // if just a string type with a decimal signature, null out the multipleOf property
                // If this is an array, then remove the array anyOf, then remove the multipleOf property, then add the singular type property.
                foreach(OpenApiSchema schemaentry in openApiDocument.Components.Schemas.Values)
                {
                    foreach(KeyValuePair<string, OpenApiSchema> schemasubentry in schemaentry.Properties)
                    {
                        //"WeightMeasure": {
                        // "type": "string",
                        // "format": "decimal",
                        // "multipleOf": 0.001,

                        OpenApiSchema thisSchema = schemasubentry.Value;
                        if ((thisSchema.Type == "string" && thisSchema.Format == "decimal") || ((thisSchema.AnyOf.Where(e => e.Type == "string").Count() > 1) && thisSchema.Format == "decimal"))
                        {
                            thisSchema.MultipleOf = null;
                        }
                    }
                }
            }
            if (bool.Parse(req.Headers["x-openapi-ifmatch"].FirstOrDefault() ?? "false"))
            {
                foreach(KeyValuePair<string, OpenApiPathItem> pathitem in openApiDocument.Paths)
                {
                    foreach(KeyValuePair<OperationType,OpenApiOperation> operation in pathitem.Value.Operations)
                    {
                        var ifmatchparam = new OpenApiParameter()
                        {
                            Name = "if-match",
                            In = ParameterLocation.Header, 
                            Required = true,
                            Schema = new OpenApiSchema() {
                                Type = "string"
                            }
                        }; 

                        ifmatchparam.AddExtension("x-ms-visibility", new OpenApiString("required"));
                        ifmatchparam.AddExtension("x-ms-summary", new OpenApiString("Place the eTag value for optimistic concurrency control in this header"));
                        
                        switch (operation.Key)
                        {
                            case OperationType.Patch : 

                                operation.Value.Parameters.Add(ifmatchparam);

                            break;

                            case OperationType.Delete : 

                                operation.Value.Parameters.Add(ifmatchparam);

                            break;
                        } 
                    }
                }
            }
            if (bool.Parse(req.Headers["x-openapi-add-metadata-etags"].FirstOrDefault() ?? "false"))
            {
                foreach (KeyValuePair<string, OpenApiSchema> schemaDictionaryEntry in openApiDocument.Components.Schemas)
                {
                    OpenApiSchema meta = new OpenApiSchema(){ Type = "object"};
                    meta.Properties.Add("etag", new OpenApiSchema()
                        {
                            Title = "The optimistic concurrency etag field, specify this in an if-match header", 
                            Nullable = true,
                            Type = "string"
                        }
                    );
                    schemaDictionaryEntry.Value.Properties.Add("__metadata", meta);
                }
            }
            if (bool.Parse(req.Headers["x-openapi-enrich-tags"].FirstOrDefault() ?? "false"))

            {
                // Monkey Patch the Missing Description for Power Platform

                foreach (OpenApiTag tag in openApiDocument.Tags) tag.Description = tag.Name; 

            }
            if (bool.Parse(req.Headers["x-openapi-pivot-for-autorest"].FirstOrDefault() ?? "false"))
            {
                    // Add unique operationId for each operation based on the 'summary' field
                    foreach(KeyValuePair<string, OpenApiPathItem> pathitem in openApiDocument.Paths)
                    {
                        foreach(KeyValuePair<OperationType,OpenApiOperation> operation in pathitem.Value.Operations)
                        {
                            operation.Value.OperationId = $"{operation.Value.Summary.Replace(" ","_")}-{pathitem.Key}";
                        }
                    }
                        // Remove the Batch Operations as AutoREST can't handle the mimetype mixed/multipart
                    openApiDocument.Paths.Remove("/$batch");
            }
            if (bool.Parse(req.Headers["x-openapi-addmetadataoperations"].FirstOrDefault() ?? "false"))
            {
                    // Add the / head and get operations
                    var pathitemroot =  new OpenApiPathItem();
                    var pathitemmeta =  new OpenApiPathItem();

                    var rootheadophead = new OpenApiOperation(){OperationId = "root/head", Summary = "The root of the API, needed for any csrf processing"};
                    rootheadophead.Responses.Add("200", new OpenApiResponse(){Description = ""});
                    pathitemroot.Operations.Add(OperationType.Head, rootheadophead);

                    var rootheadopget = new OpenApiOperation(){OperationId = "root/get", Summary = "The root of the API"};
                    rootheadopget.Responses.Add("200", new OpenApiResponse(){Description = ""});
                    pathitemroot.Operations.Add(OperationType.Get, rootheadopget);

                    openApiDocument.Paths.Add("/", pathitemroot);

                    var rootheadopmeta = new OpenApiOperation(){OperationId = "$metadata/get", Summary = "$Metadata endpoint"};
                    rootheadopmeta.Responses.Add("200", new OpenApiResponse(){Description = ""});
                    pathitemmeta.Operations.Add(OperationType.Get, rootheadopmeta);

                    openApiDocument.Paths.Add("/$metadata", pathitemmeta);
            }
            if (bool.Parse(req.Headers["x-openapi-adddiagnosticoperation"].FirstOrDefault() ?? "false"))
            {

                // add the /diagnostic operation for testing only
                var pathitemdiag =  new OpenApiPathItem();
                var rootheadopget = new OpenApiOperation(){OperationId = "diagnostic/get", Summary = "The optional diagnotics policy for caching"};
                rootheadopget.Responses.Add("200", new OpenApiResponse(){Description = ""});
                pathitemdiag.Operations.Add(OperationType.Get, rootheadopget);
                openApiDocument.Paths.Add("/diagnostic", pathitemdiag);

            }
            if (bool.Parse(req.Headers["x-openapi-truncate-description"].FirstOrDefault() ?? "false"))
            {
                // Info/Description parse and truncate to remove the EDM 
                openApiDocument.Info.Description = openApiDocument.Info.Description.Substring(0,openApiDocument.Info.Description.IndexOf("\n"));
            }

            string transformfinal = ""; 
            if (req.Headers["x-openapi-version"].FirstOrDefault()?.StartsWith("3.0") ?? true)
            {
                transformfinal = openApiDocument.Serialize(OpenApiSpecVersion.OpenApi3_0, OpenApiFormat.Json);
            }
            else
            {
                transformfinal = openApiDocument.Serialize(OpenApiSpecVersion.OpenApi2_0, OpenApiFormat.Json);
            }
            
            return new OkObjectResult(transformfinal);
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