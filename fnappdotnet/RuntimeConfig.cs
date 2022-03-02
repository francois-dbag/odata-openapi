using System.Xml.Xsl; 
namespace OpenApi.Converter {
    public interface IRuntimeConfigAndTransforms
    {
        XslCompiledTransform v2toV4xsl {get;set;}
        XslCompiledTransform v4CSDLToOpenAPIXslt  {get;set;}
        XslCompiledTransform CSDLToODataVersion {get;set;}
    }
    public class RuntimeConfigAndTransforms : IRuntimeConfigAndTransforms
    {
        public XslCompiledTransform v2toV4xsl  {get;set;}
        public XslCompiledTransform v4CSDLToOpenAPIXslt  {get;set;}
        public XslCompiledTransform CSDLToODataVersion  {get;set;}
    }
}