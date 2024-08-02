using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Cosmos;
using System.Linq;

namespace Company.Function
{
    public static class UpdateProductQuantity
    {
        private static readonly string EndpointUri = "https://<cosmos_account>.documents.azure.com:443/";
        private static readonly string PrimaryKey = "<primary_key>";
        private static readonly string DatabaseId = "ProductDB";
        private static readonly string ContainerId = "Products";

        private static CosmosClient cosmosClient;
        private static Database database;
        private static Container container;

        static UpdateProductQuantity()
        {
            cosmosClient = new CosmosClient(EndpointUri, PrimaryKey);
            database = cosmosClient.GetDatabase(DatabaseId);
            container = database.GetContainer(ContainerId);
        }

        [FunctionName("UpdateProductQuantity")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string productId = data?.productId;
            int? newQuantity = data?.newQuantity;

            if (string.IsNullOrEmpty(productId) || newQuantity == null)
            {
                return new BadRequestObjectResult("Invalid input.");
            }

            try
            {
                var queryDefinition = new QueryDefinition("SELECT * FROM c WHERE c.productId = @productId")
                    .WithParameter("@productId", productId);

                var queryIterator = container.GetItemQueryIterator<dynamic>(queryDefinition);

                dynamic productItem = null;

                if (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    productItem = response.FirstOrDefault();
                }

                if (productItem == null)
                {
                    return new NotFoundObjectResult($"Product not found: {productId}");
                }

                // Update the quantity
                productItem.quantity = newQuantity;
                
                // Retrieve the 'id' from the productItem
                string id = productItem.id;

                if (string.IsNullOrEmpty(id))
                {
                    log.LogError($"The product item with productId {productId} does not contain an 'id' field.");
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }

                // Replace the item in Cosmos DB
                await container.ReplaceItemAsync<dynamic>(productItem, id, new PartitionKey(productId));

                return new OkObjectResult($"Product updated: {productId} with new quantity: {newQuantity}");
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new NotFoundObjectResult($"Product not found: {productId}");
            }
            catch (Exception ex)
            {
                log.LogError($"Could not update product: {ex.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}