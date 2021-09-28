module.exports = async function (context, req) {
    
    const csdl = require("odata-csdl");
    const lib = require("../lib/csdl2openapi.js");
    const xml = req.body;
    const json = csdl.xml2json(xml, false);

    const openapi = lib.csdl2openapi(json, {
      scheme: req.headers['x-scheme'] || "https",
      host: req.headers['x-host'] || "services.odata.org",
      basePath: req.headers['x-basePath'] || "/service-root",
      diagram: true,
    });

    const responseMessage = JSON.stringify(openapi, null, 4);

    context.res = {
        status: 200,
        headers: {'content-type': 'application/json'},
        body: responseMessage
    };

}