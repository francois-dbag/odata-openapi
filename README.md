<div>
<h2>README</h2>

This work is a simple hosting container for the OASIS OData-openapi js executable wrapped in a nodeJs running Azure Function App.

It exists solely to demonstrate how to provide a simple HTML5 wrapper around the OASIS code repository, primarily for the purpose of making it easier to work with SAP odata services and their XML metadata when wanting to publish them through Azure API Management.
 
You can see it in action, and convert odata definitions to openapi specifications [here](https://aka.ms/ODataToOpenAPI).
 
Please feel free to deploy it yourself via a combination of a Azure Functions Consumption App (Source is in the /FunctionApp/ folder) and a web-enabled Azure Storage Account (Source is in the /SPA/ folder).

Please raise any issues and feature requests via Github Issues.
