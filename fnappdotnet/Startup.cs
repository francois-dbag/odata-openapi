using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Xml.Xsl; 
using System; 
using System.IO;
[assembly: FunctionsStartup(typeof(OpenApi.Converter.Startup))]
namespace OpenApi.Converter
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            string rootpath = (bool.Parse(System.Environment.GetEnvironmentVariable("OverrideLocalTransformFilesInRoot") ?? "false") ? Path.Combine(System.Environment.GetEnvironmentVariable("HOME")) : Path.Combine(System.Environment.GetEnvironmentVariable("HOME"), "site", "wwwroot"));

            builder.Services.AddSingleton<IRuntimeConfigAndTransforms>((s) => {
                return new RuntimeConfigAndTransforms(){
                    v2toV4xsl = LoadTransform(Path.Combine(rootpath, "V2-to-V4-CSDL.xsl")),
                    v4CSDLToOpenAPIXslt = LoadTransform(Path.Combine(rootpath, "V4-CSDL-to-OpenAPI.xsl")),
                    CSDLToODataVersion = LoadTransform(Path.Combine(rootpath, "OData-Version.xsl"))
                };
            });
        }
        public XslCompiledTransform LoadTransform(string path){
            var t = new XslCompiledTransform(); 
            t.Load(path);
            return t; 
        }
    }
}