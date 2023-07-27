using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
// using System.Globalization; 
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
// using NSwag.CodeGeneration.CSharp;

namespace OpenApi.Converter {
    public class OpenAPIConverterAndPatches {
        private readonly IRuntimeConfigAndTransforms configAndTransforms = null;
        public OpenAPIConverterAndPatches(IRuntimeConfigAndTransforms configandtransforms)
        {
            configAndTransforms = configandtransforms;
        }
        [FunctionName("ConvertOdataToOpenAPI")]
        public async Task<IActionResult> RunOpenAPI([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req, ILogger log) {
            if(req.Headers["x-odata-as-json"].FirstOrDefault() ?? "" != "")
            {
                return new OkObjectResult(await ProcessJSONSchema(req)); 
            }
            else
            {
                return new OkObjectResult(await ProcessOpenAPI(req)); 
            }
        }
        // [FunctionName("ConvertODataToCSharpNSwag")]
        // public async Task<IActionResult> RunCodeCSharp([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req, ILogger log) {

        //     string openApiText = await ProcessOpenAPI(req); 
        //     var settings = new CSharpClientGeneratorSettings
        //     {
        //         ClassName = "MyClass", 
        //         CSharpGeneratorSettings = 
        //         {
        //             Namespace = (req.Headers["x-basepath"].FirstOrDefault() ?? "") == "" ? "MyNamespace" : req.Headers["x-basepath"].FirstOrDefault()
        //         }
        //     };

        //     var openApiModel = await NSwag.OpenApiDocument.FromJsonAsync(openApiText);
        //     openApiModel.GenerateOperationIds();
        //     var generator = new CSharpClientGenerator(openApiModel, settings);	
        //     var code = generator.GenerateFile();
        //     return new OkObjectResult(code); 

        // }

        public async Task<string> ProcessJSONSchema(HttpRequest req)
        {
            string inputData = await req.ReadAsStringAsync();
            string v4OdataXml = "";
            string inputVersion = ApplyTransform(inputData, configAndTransforms.CSDLToODataVersion);
            if (!inputVersion.StartsWith("4"))
            {
                v4OdataXml = ApplyTransform(inputData, configAndTransforms.v2toV4xsl);
            }
            else
            {
                v4OdataXml = inputData; 
            }
             XsltArgumentList args = new XsltArgumentList();
             return ApplyTransform(v4OdataXml, configAndTransforms.v4CSDLtoJSONSchema, args);
        }
        public async Task<string> ProcessOpenAPI(HttpRequest req)
        {
            string inputData = await req.ReadAsStringAsync();
            string v4OdataXml = "";
            string inputVersion = ApplyTransform(inputData, configAndTransforms.CSDLToODataVersion);
            if (!inputVersion.StartsWith("4"))
            {
                v4OdataXml = ApplyTransform(inputData, configAndTransforms.v2toV4xsl);
            }
            else
            {
                v4OdataXml = inputData; 
            }
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
            
            if (bool.Parse(req.Headers["x-openapi-enrich-security"].FirstOrDefault() ?? "false"))
            {
                OpenApiSecurityScheme secscheme = new OpenApiSecurityScheme();
                secscheme.Description = "OAuth Client Login (With Azure Active Directory)";
                secscheme.Type = SecuritySchemeType.OAuth2;
                secscheme.BearerFormat = "JWT";
                secscheme.Reference = new OpenApiReference() 
                {
                    Id = "AADToken",
                    Type = ReferenceType.SecurityScheme
                }; 
                OpenApiOAuthFlows oaoaf = new OpenApiOAuthFlows() {};
                oaoaf.Implicit = new OpenApiOAuthFlow();
                string tenant = req.Headers["x-openapi-aad-tenant"].FirstOrDefault() ?? "";
                secscheme.OpenIdConnectUrl = new System.Uri($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/.well-known/openid-configuration");
                oaoaf.Implicit.AuthorizationUrl = new System.Uri($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize");
                
                //oaoaf.Implicit.TokenUrl = new System.Uri($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token");
                //oaoaf.Implicit.RefreshUrl =  new System.Uri($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token");
                
                oaoaf.ClientCredentials = new OpenApiOAuthFlow();
                //oaoaf.ClientCredentials.AuthorizationUrl = new System.Uri($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize");
                oaoaf.ClientCredentials.TokenUrl = new System.Uri($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token");
                //oaoaf.ClientCredentials.RefreshUrl =  new System.Uri($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token");
                
                oaoaf.Implicit.Scopes = new Dictionary<string,string>();
                oaoaf.Implicit.Scopes.Add("userid", "Retrieve User Information");
                oaoaf.Implicit.Scopes.Add("profile", "Access the users profile");
                oaoaf.Implicit.Scopes.Add("user_impersonation","Access the signed in system as the user"); 
                oaoaf.ClientCredentials.Scopes = new Dictionary<string,string>();
                oaoaf.ClientCredentials.Scopes.Add("userid", "Retrieve User Information");
                oaoaf.ClientCredentials.Scopes.Add("profile", "Access the users profile");
                oaoaf.ClientCredentials.Scopes.Add("user_impersonation","Access the signed in system as the user"); 
                secscheme.Flows = oaoaf; 
                openApiDocument.Components.SecuritySchemes = new Dictionary<string, OpenApiSecurityScheme>();
                openApiDocument.Components.SecuritySchemes.Add(secscheme.Reference.Id, secscheme);
                OpenApiSecurityRequirement opensr = new OpenApiSecurityRequirement();
                opensr.Add(secscheme, new List<string>() {"userid", "profile","user_impersonation"});
                openApiDocument.SecurityRequirements = new List<OpenApiSecurityRequirement>();
                openApiDocument.SecurityRequirements.Add(opensr);
            }
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
                        Title = "Optimistic concurrency etag, use in if-match header", Nullable = true, Type = "string"
                    });
                    schemaDictionaryEntry.Value.Properties.Add("__metadata", meta);
                }
            }
            if (bool.Parse(req.Headers["x-openapi-enrich-tags"].FirstOrDefault() ?? "false")){ // Copy the description over to the tags
                foreach (OpenApiTag tag in openApiDocument.Tags) tag.Description = tag.Name; 
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
                OpenApiOperation diagHeadOpGet = new OpenApiOperation(){OperationId = "diagnostic/get", Summary = "Caching Policy Diagnostics Endpoint"};
                diagHeadOpGet.Responses.Add("200", new OpenApiResponse(){Description = "Caching Diagnostics Log Data"});
                pathItemDiag.Operations.Add(OperationType.Get, diagHeadOpGet);
                openApiDocument.Paths.Add("/diagnostic", pathItemDiag);
            }
            if (bool.Parse(req.Headers["x-openapi-truncate-description"].FirstOrDefault() ?? "false")){ // Info/Description parse and truncate to remove the EDM and keep us below 1000 chars.
                openApiDocument.Info.Description = openApiDocument.Info.Description.Substring(0,openApiDocument.Info.Description.IndexOf("\n"));
            }
           
            if (bool.Parse(req.Headers["x-openapi-pivot-for-autorest"].FirstOrDefault() ?? "false")){
                foreach(KeyValuePair<string, OpenApiPathItem> pathitem in openApiDocument.Paths){// Add unique operationId for each operation based on the 'summary' field
                    foreach(KeyValuePair<OperationType,OpenApiOperation> operation in pathitem.Value.Operations){

                        // Make the text more readable in the operationid for a user / programmer 
                        operation.Value.OperationId = //(new CultureInfo("en-US", false).TextInfo)
                            $"{operation.Value.Summary}__{pathitem.Key}"
                            .Replace("entities from", "EntitiesFrom")
                            .Replace("{","")
                            .Replace("}","")
                            .Replace("'","")
                            .Replace(".","Dot")
                            .Replace(",","Comma")
                            .Replace("@","At")
                            .Replace("#","Hash")
                            .Replace("*", "Star")
                            .Replace("\\", "BackSlash")
                            .Replace("%", "Percent")
                            .Replace("/", "Slash")
                            .Replace("(","")
                            .Replace(")","")
                            .Replace(" ","")
                            .Replace("$","Dollar")
                            .Replace("-","")
                            .Replace("_","");

                        // If we are enriching tags and outputting swagger v2, then we are probably sending V2 Swagger to PowerPlatform or v3 to autorest
                        // Swagger v2 does not define a 4xx response (it's allowed in V3 OpenAPI, but Autorest doesn't like it), so patch 4xx to 400.

                        OpenApiResponse oar = null;
                        if (operation.Value.Responses.TryGetValue("4XX", out oar))
                        {
                            operation.Value.Responses.Remove("4XX");
                            operation.Value.Responses.Add("400", oar);
                            operation.Value.Responses.Add("401", new OpenApiResponse() {Description = "Not Authorized"});
                            operation.Value.Responses.Add("403", new OpenApiResponse() {Description = "Forbidden"});
                            operation.Value.Responses.Add("404", new OpenApiResponse() {Description = "Not Found"});
                            operation.Value.Responses.Add("503", new OpenApiResponse() {Description = "Internal Server Error"});
                        }
                    }
                }
                openApiDocument.Paths.Remove("/$batch"); // Remove the Batch Operations as AutoREST can't handle the mimetype mixed/multipart type yet
            }

            OpenApiSpecVersion spec = OpenApiSpecVersion.OpenApi3_0; // Export in the correct format OpenAPI 3.0 or swagger 2.0
            if (req.Headers["x-openapi-version"].FirstOrDefault()?.StartsWith("2.0") ?? false) {
                spec = OpenApiSpecVersion.OpenApi2_0;
            }
            return openApiDocument.Serialize(spec, OpenApiFormat.Json);
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