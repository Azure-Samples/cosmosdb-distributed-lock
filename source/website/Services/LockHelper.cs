using CosmosDistributedLock;
using CosmosDistributedLock.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace Versioning
{
    public class LockHelper
    {
        private readonly CosmosClient client;
        private Database? database;
        private Container? container;

        private string databaseName = "LockDB";
        private string containerName = "Locks";
        private string partitionKey = "/id";

        public LockHelper(){

            string uri = "https://mjb-lock.documents.azure.com:443/";
            string key = "h4jwVSCvzjTMtfvjQEGu9ULH2UyEQhs1ZfdxH0sStDqt4Z6WJtkFA2y4rmYJ0vNtRJHsGt9ZU8EGACDbbHTUhw==";
            client = new CosmosClient(
                accountEndpoint: uri,
                authKeyOrResourceToken: key);

            container = client.GetDatabase(databaseName).GetContainer(containerName);
        }

        //public async Task Setup()
        //{
        //    database = await client.CreateDatabaseIfNotExistsAsync(id: databaseName);
        //    container = await database.CreateContainerIfNotExistsAsync(
        //        id: containerName,
        //        partitionKeyPath: partitionKey,
        //        throughput: 400
        //    );
        //}

        public async Task ReleaseLock(DistributedLock gLock) {

            //await Setup();

            await container.DeleteItemAsync<DistributedLock>(
                    id: gLock.LockName,
                    partitionKey: new PartitionKey(gLock.LockName)
                );
        }

        public async Task<IEnumerable<DistributedLock>> RetrieveAllLocksAsync(){
            
            //await Setup();

            List<DistributedLock> locks = new();
            using FeedIterator<DistributedLock> feed = container.GetItemQueryIterator<DistributedLock>(
                queryText: "SELECT * FROM Locks"
            );
            
            while (feed.HasMoreResults)
            {
                FeedResponse<DistributedLock> response = await feed.ReadNextAsync();

                // Iterate query results
                foreach (DistributedLock gLock in response)
                {
                    locks.Add(gLock);
                }
            }
            return locks;
        }

        public async Task<DistributedLock> RetrieveLockAsync(string lockName){
            
            //await Setup();

            IOrderedQueryable<DistributedLock> ordersQueryable = container.GetItemLinqQueryable<DistributedLock>();
            var matches = ordersQueryable
                .Where(order => order.LockName == lockName);
            
            using FeedIterator<DistributedLock> orderFeed = matches.ToFeedIterator();

            DistributedLock selectedOrder = new DistributedLock();
            
            while (orderFeed.HasMoreResults)
            {
                FeedResponse<DistributedLock> response = await orderFeed.ReadNextAsync();
                if (response.Count > 0)
                {
                    selectedOrder = response.Resource.First();
                }
            }
            
            //return orderResponse.Resource;
            return selectedOrder;
        }

        public async Task<DistributedLock> SaveLock(DistributedLock gLock)
        {
            //await Setup();

            //try to create the lock
            //var mutex = await Lock.CreateLock(client, databaseName, containerName, gLock, 1);

            //var seenToken = this.globalToken;

            //try to acquire lease...
            //var localToken = await mutex.AcquireLease(gLock.Ttl.Value);

            //gLock.Token = localToken;

            return gLock;
        }
    }
}