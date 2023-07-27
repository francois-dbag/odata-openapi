# 📖Introduction

This work is a simple hosting container for the OASIS OData-openapi js executable wrapped in a nodeJs running Azure Function App. In addition to OpenAPI conversion it offers OData metadata JSON translation based of the OASIS [odata-json-schema repos](https://github.com/oasis-tcs/odata-json-schema).

It exists solely to demonstrate how to provide a simple HTML5 wrapper around the OASIS code repository, primarily for the purpose of making it easier to work with SAP OData services and their XML metadata when wanting to publish them through Azure API Management.

## 💡How-To

You can see it in action, convert OData definitions to OpenAPI specifications [here](https://aka.ms/ODataOpenAPI), and generate SDKs for popular programming languages.

Find more details on the SDK generation on the [Kiota website](https://microsoft.github.io/kiota/). We decided to use Kiota instead of [AutoREST](https://github.com/Azure/autorest) because of its greater flexibility for our project.

Please feel free to deploy it yourself via a combination of a Azure Functions Consumption App (Source is in the /FunctionApp/ folder) and a web-enabled Azure Storage Account (Source is in the /SPA/ folder).

## 🤟🏾Contributing

Please raise any issues and feature requests via [Github Issues](https://github.com/WillEastbury/odata-openapi/issues).
