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
using System.Collections.Generic;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Any;
namespace OpenApi.Converter {
    public class OpenAPIConverterAndPatches {
        
        private readonly IRuntimeConfigAndTransforms configAndTransforms = null;
        public OpenAPIConverterAndPatches(IRuntimeConfigAndTransforms configandtransforms)
        {
            configAndTransforms = configandtransforms;
        }
        [FunctionName("HttpTrigger1")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req, ILogger log) {
            string inputData = await req.ReadAsStringAsync(); 
            string v4OdataXml = ApplyTransform(inputData, configAndTransforms.v2toV4xsl);
            string inputVersion = ApplyTransform(inputData, configAndTransforms.CSDLToODataVersion);
            XsltArgumentList args = new XsltArgumentList();
            args.AddParam("scheme","", (req.Headers["x-scheme"].FirstOrDefault() ?? "") == "" ? "https" : req.Headers["x-scheme"].FirstOrDefault());
            args.AddParam("host","", (req.Headers["x-host"].FirstOrDefault() ?? "") == "" ? "services.odata.org" : req.Headers["x-host"].FirstOrDefault());
            args.AddParam("basePath","", (req.Headers["x-basepath"].FirstOrDefault() ?? "") == "" ? "/service-root" : req.Headers["x-basepath"].FirstOrDefault());
            args.AddParam("odata-version","", inputVersion); 
            args.AddParam("diagram","","YES");
            args.AddParam("openapi-root","", "https://raw.githubusercontent.com/oasis-tcs/odata-openapi/master/examples/");     
            args.AddParam("openapi-version","" , "3.0.0"); 
            string openapitext = ApplyTransform(v4OdataXml, configAndTransforms.v4CSDLToOpenAPIXslt, args);
            OpenApiDocument openApiDocument = new OpenApiStringReader().Read(openapitext, out var diagnostic);
            if (bool.Parse(req.Headers["x-openapi-enrich-decimals"].FirstOrDefault() ?? "false")){ // PowerPlatform doesn't like the multipleOf property on decimal types
                foreach(OpenApiSchema schemaentry in openApiDocument.Components.Schemas.Values){
                    foreach(KeyValuePair<string, OpenApiSchema> schemasubentry in schemaentry.Properties){
                        OpenApiSchema thisSchema = schemasubentry.Value; // If just a string type with a decimal signature, null out the multipleOf property
                        if  ((thisSchema.Type == "string" && thisSchema.Format == "decimal") || ((thisSchema.AnyOf.Where(e => e.Type == "string").Count() > 1) && thisSchema.Format == "decimal")){
                            thisSchema.MultipleOf = null;
                        }
                    }
                }
            }
            if (bool.Parse(req.Headers["x-openapi-ifmatch"].FirstOrDefault() ?? "false")){ // Add the if-match header for concurrency control if needed
                foreach(KeyValuePair<string, OpenApiPathItem> pathitem in openApiDocument.Paths){
                    foreach(KeyValuePair<OperationType,OpenApiOperation> operation in pathitem.Value.Operations){
                        var ifMatchParam = new OpenApiParameter(){
                            Name = "if-match", In = ParameterLocation.Header, Required = true,
                                Schema = new OpenApiSchema() {
                                    Type = "string"
                        }}; 
                        ifMatchParam.AddExtension("x-ms-visibility", new OpenApiString("required")); // Make this field appear in powerplatform's gui for the connector
                        ifMatchParam.AddExtension("x-ms-summary", new OpenApiString("Place the eTag value for optimistic concurrency control in this header"));
                        switch (operation.Key){
                            case OperationType.Patch : 
                                operation.Value.Parameters.Add(ifMatchParam); break;
                            case OperationType.Delete : 
                                operation.Value.Parameters.Add(ifMatchParam); break;
                        } 
                    }
                }
            }
            if (bool.Parse(req.Headers["x-openapi-add-metadata-etags"].FirstOrDefault() ?? "false")){ // Add the etag entries to the schema so optimistic concurrency can work
                foreach (KeyValuePair<string, OpenApiSchema> schemaDictionaryEntry in openApiDocument.Components.Schemas){
                    OpenApiSchema meta = new OpenApiSchema(){ Type = "object"};
                    meta.Properties.Add("etag", new OpenApiSchema(){
                        Title = "optimistic concurrency etag, use in if-match header", Nullable = true, Type = "string"
                    });
                    schemaDictionaryEntry.Value.Properties.Add("__metadata", meta);
                }
            }
            if (bool.Parse(req.Headers["x-openapi-enrich-tags"].FirstOrDefault() ?? "false")){ // Copy the description over to the tags
                foreach (OpenApiTag tag in openApiDocument.Tags) tag.Description = tag.Name; 
            }
            if (bool.Parse(req.Headers["x-openapi-pivot-for-autorest"].FirstOrDefault() ?? "false")){
                foreach(KeyValuePair<string, OpenApiPathItem> pathitem in openApiDocument.Paths){// Add unique operationId for each operation based on the 'summary' field
                    foreach(KeyValuePair<OperationType,OpenApiOperation> operation in pathitem.Value.Operations){
                        operation.Value.OperationId = $"{operation.Value.Summary.Replace(" ","_")}-{pathitem.Key}";
                    }
                }
                openApiDocument.Paths.Remove("/$batch"); // Remove the Batch Operations as AutoREST can't handle the mimetype mixed/multipart type yet
            }
            if (bool.Parse(req.Headers["x-openapi-addmetadataoperations"].FirstOrDefault() ?? "false")){ // Add the special metadata / head and get operations
                OpenApiPathItem pathItemRoot = new OpenApiPathItem(), pathItemMeta = new OpenApiPathItem();
                OpenApiOperation rootHeadOpHead = new OpenApiOperation(){OperationId = "root/head", Summary = "The root of the API, needed for any csrf processing"};
                rootHeadOpHead.Responses.Add("200", new OpenApiResponse(){Description = ""});
                pathItemRoot.Operations.Add(OperationType.Head, rootHeadOpHead);
                OpenApiOperation rootHeadOpGet = new OpenApiOperation(){OperationId = "root/get", Summary = "The root of the API"};
                rootHeadOpGet.Responses.Add("200", new OpenApiResponse(){Description = ""});
                pathItemRoot.Operations.Add(OperationType.Get, rootHeadOpGet);
                openApiDocument.Paths.Add("/", pathItemRoot);
                OpenApiOperation rootHeadOpMeta = new OpenApiOperation(){OperationId = "$metadata/get", Summary = "$Metadata endpoint"};
                rootHeadOpMeta.Responses.Add("200", new OpenApiResponse(){Description = ""});
                pathItemMeta.Operations.Add(OperationType.Get, rootHeadOpMeta);
                openApiDocument.Paths.Add("/$metadata", pathItemMeta);
            }
            if (bool.Parse(req.Headers["x-openapi-adddiagnosticoperation"].FirstOrDefault() ?? "false")){ // add the /diagnostic operation for testing only
                OpenApiPathItem pathItemDiag =  new OpenApiPathItem();
                OpenApiOperation diagHeadOpGet = new OpenApiOperation(){OperationId = "diagnostic/get", Summary = "The optional diagnostics policy for caching"};
                diagHeadOpGet.Responses.Add("200", new OpenApiResponse(){Description = "Caching Diagnostics Log Data"});
                pathItemDiag.Operations.Add(OperationType.Get, diagHeadOpGet);
                openApiDocument.Paths.Add("/diagnostic", pathItemDiag);
            }
            if (bool.Parse(req.Headers["x-openapi-truncate-description"].FirstOrDefault() ?? "false")){ // Info/Description parse and truncate to remove the EDM and keep us below 1000 chars.
                openApiDocument.Info.Description = openApiDocument.Info.Description.Substring(0,openApiDocument.Info.Description.IndexOf("\n"));
            }
            OpenApiSpecVersion spec = OpenApiSpecVersion.OpenApi3_0; // Export in the correct format OpenAPI 3.0 or swagger 2.0
            if (req.Headers["x-openapi-version"].FirstOrDefault()?.StartsWith("2.0") ?? false) spec = OpenApiSpecVersion.OpenApi2_0;
            string output = openApiDocument.Serialize(spec, OpenApiFormat.Json);
            return new OkObjectResult(output); 
        }
        public static string ApplyTransform(string input, XslCompiledTransform xslTrans, XsltArgumentList args = null) { // Apply an XSLT transform
            using (StringReader str = new StringReader(input) ){
                using (XmlReader xr = XmlReader.Create(str)) {
                    using (StringWriter sw = new StringWriter()){
                        xslTrans.Transform(xr, args, sw);
                        return sw.ToString();
            }}}
        }
    }
}