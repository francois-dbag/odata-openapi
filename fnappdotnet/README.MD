This version of the converter has hosting in c# to run the xslt translations documented by OASIS then uses the 
Microsoft OpenAPI libraries to post-process the JSON output of the OpenAPI file and convert it to v2 swagger or v3.

This version can also work with any OData import format from v2 onwards as it is xslt based. 

Note we are currently pinning the OpenAPI.net vesion to a preview version due to a big when being used from dotnet6 in the 1.2.3 version of the library.