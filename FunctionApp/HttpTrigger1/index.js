module.exports = async function (context, req) {
    
    const csdl = require("odata-csdl");
    const xslt4node = require('xslt4node');
    const path = require('path');
    const lib = require("../lib/csdl2openapi.js");
    const xml = req.body;
    const json = xml.startsWith("<") ? csdl.xml2json(xml, false) : JSON.parse(xml);

    var statusCode = 200;
    var responseMessage = "OK";
    var odataVersionTrace = "not set";
    var openapi;
    //check OData version to feed to correct target conversion approach as described by Oasis
    if (json.$Version < "3.0") {
      odataVersionTrace = "OData version < 3.0 converting to v4 using xslt4node";
      var xsltpath = path.dirname(require.main.filename) + path.sep;
      xslt4node.addLibrary(xsltpath + 'lib/xalan/xalan.jar');

      xslt4node.transform(
        {
            xsltPath: xsltpath + 'OData-Version.xsl',
            sourcePath: xml,
            result: String
        },
        (err, result) => {
            if (err) {
              statusCode = 500;
              responseMessage = 'Unable to deserialize XML: ' + err.message;
            } else if (result == "") {
              statusCode = 500;
              responseMessage = 'Unable to parse input as OData v2: ' + err.message;
            } else{
              xslt4node.transform(
                {
                    xsltPath: xsltpath + 'V2-to-V4-CSDL.xsl',
                    sourcePath: xml,
                    result: target
                },
                (err, result) => {
                    if (err) {
                      statusCode = 500;
                      responseMessage = 'Unable to convert OData v2 to v4: ' + err.message;
                    } else {
                      json = target;
                    }
                }
              );
            }
      });
    }else{
      odataVersionTrace = "OData version >= 3.0 using csdl2openapi converter";
    }
    //do conversion only if no errors
    if(statusCode == 200){
      openapi = lib.csdl2openapi(json, {
        scheme: req.headers['x-scheme'] || "https",
        host: req.headers['x-host'] || "services.odata.org",
        basePath: req.headers['x-basepath'] || "/service-root",
        diagram: true,
      });
      responseMessage = JSON.stringify(openapi, null, 4);
    }
    //return result
    context.res = {
        status: statusCode,
        headers: {'content-type': 'application/json', 'odata-version-trace': odataVersionTrace},
        body: responseMessage
    };

}

