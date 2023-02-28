using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using System.Net;
using Container = Microsoft.Azure.Cosmos.Container;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace Cosmos_Patterns_GlobalLock
{
    /// <summary>
    /// This represents a lock in the Cosmos DB.  Also used as the target of the lock.
    /// </summary>
    public class LockDocument 
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("_etag")]
        public string ETag { get; set; }
        
        [JsonProperty("_ts")]
        public long Ts { get; set; }

        public string EntityType { get; set; }
        
        public string Owner { get; set; }

        public int Token { get; set; }

        [JsonProperty("ttl")]
        public int? Ttl { get; set; }
    }

    public class Lock
    {
        CosmosClient client;
        
        string lockDbName;
        Database lockDb;

        string lockContainerName;
        Container lockContainer;
        
        string paritionKeyPath = "/id";

        string lockName;
        
        public string ownerId;
        
        readonly int refreshIntervalS;

        public int defaultTtl = 60;

        /// <summary>
        /// This creates a container that has the TTL feature enabled.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="lockDbName"></param>
        /// <param name="lockContainerName"></param>
        /// <param name="lockName"></param>
        /// <param name="refreshIntervalS"></param>
        public Lock(CosmosClient client, string lockDbName, string lockContainerName, string lockName, int refreshIntervalS)
        {
            this.client = client;
            
            this.lockDbName = lockDbName;
            this.lockContainerName = lockContainerName;
            
            this.lockName = lockName;
            this.refreshIntervalS = refreshIntervalS;

            this.ownerId = Guid.NewGuid().ToString();

            //init the database and container
            lockDb = client.GetDatabase(lockDbName);

            //must use this method to create a container with TTL enabled...
            ContainerProperties cProps = new ContainerProperties();
            cProps.Id = lockContainerName;
            cProps.PartitionKeyPath = paritionKeyPath;
            cProps.DefaultTimeToLive = defaultTtl;

            ThroughputProperties tProps = ThroughputProperties.CreateManualThroughput(400);

            try
            {
                //check if exists...
                Container container = lockDb.GetContainer(cProps.Id);

                if (container == null)
                {
                    lockContainer = lockDb.CreateContainerAsync(
                        cProps, tProps
                    ).Result;
                }
                else
                    lockContainer = container;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// Simple static constructor
        /// </summary>
        /// <param name="client"></param>
        /// <param name="lockDb"></param>
        /// <param name="lockContainer"></param>
        /// <param name="lockName"></param>
        /// <param name="refreshIntervalS"></param>
        /// <returns></returns>
        static public async Task<Lock> CreateLock(CosmosClient client, string lockDb, string lockContainer, string lockName, int refreshIntervalS)
        {
            return new Lock(client, lockDb, lockContainer, lockName, refreshIntervalS);
        }

        /// <summary>
        /// This function will check for a lease object (if it exists).  If it does, it checks to see if the current client has the lease.  If the lease is expired, it will automatically be deleted by Cosmos DB via the TTL property.
        /// </summary>
        /// <param name="leaseDurationS"></param>
        /// <returns></returns>
        public async Task<int> AcquireLease(int leaseDurationS)
        {
            DateTime started = DateTime.Now;

            while (true)
            {
                LockDocument lockRecord = null;

                try
                {
                    //Check to see if a Lock with the target name exists.
                    lockRecord = await lockContainer.ReadItemAsync<LockDocument>(
                        id: lockName,
                        partitionKey: new PartitionKey(lockName)
                    );
                }
                catch (CosmosException e)
                {
                    //If it is not found, then create a new lease
                    if (e.StatusCode == HttpStatusCode.NotFound)
                    {
                        lockRecord = new LockDocument();
                        lockRecord.Id = ownerId;
                        lockRecord.Ttl = leaseDurationS;
                        
                        //this is the TTL one
                        await lockContainer.UpsertItemAsync<LockDocument>(
                            item: lockRecord,
                            partitionKey: new PartitionKey(lockRecord.Id)
                        );                        

                        try
                        {
                            //this is the current token object (used to keep track if current lease if valid or not
                            lockRecord = new LockDocument();
                            lockRecord.Id = lockName;
                            lockRecord.Owner = this.ownerId;
                            lockRecord.Token = 1;
                            lockRecord.Ttl = -1;

                            await lockContainer.UpsertItemAsync<LockDocument>(
                                item: lockRecord,
                                partitionKey: new PartitionKey(lockRecord.Id)
                            );

                            return 1;
                        }
                        catch
                        {
                            await Task.Delay(this.refreshIntervalS * 1000);
                            continue;
                        }
                    }

                    throw;
                }

                //Since a lock exists, check to see if we are the one that has the lock
                string owner = lockRecord.Owner;
                int token = lockRecord.Token;

                //Are we the owner of the lock?
                if (this.ownerId.Equals(owner))
                {
                    lockRecord = new LockDocument();
                    lockRecord.Id = this.ownerId;
                    lockRecord.Ttl = leaseDurationS;

                    await lockContainer.UpsertItemAsync<LockDocument>(
                        item: lockRecord,
                        partitionKey: new PartitionKey(lockRecord.Id)
                    );

                    return token;
                }

                bool shouldTryAcquire = true;

                //We were not the owner
                if (!string.IsNullOrEmpty(owner))
                {
                    try
                    {
                        lockRecord = await lockContainer.ReadItemAsync<LockDocument>(
                            id: owner,
                            partitionKey: new PartitionKey(owner)
                        );

                        shouldTryAcquire = false;
                    }
                    catch (CosmosException e)
                    {
                        if (e.StatusCode != HttpStatusCode.NotFound)
                        {
                            throw;
                        }
                    }
                }

                //No owner found, try to aquire the lease
                if (shouldTryAcquire)
                {
                    lockRecord = new LockDocument();
                    lockRecord.Id = ownerId;
                    lockRecord.Ttl = leaseDurationS;

                    await lockContainer.UpsertItemAsync<LockDocument>(
                        item: lockRecord,
                        partitionKey: new PartitionKey(lockRecord.Id)
                    );

                    try
                    {
                        LockDocument newLockRecord = new LockDocument();
                        newLockRecord.Id = lockName;
                        newLockRecord.Owner = this.ownerId;
                        newLockRecord.Token = token + 1;
                        newLockRecord.Ttl = -1;

                        ItemRequestOptions ro = new ItemRequestOptions { IfMatchEtag = lockRecord.ETag };
                        
                        await lockContainer.ReplaceItemAsync<LockDocument>(
                            item: newLockRecord,
                            id: newLockRecord.Id,
                            requestOptions: ro,
                            partitionKey: new PartitionKey(newLockRecord.Id)
                        );
                    }
                    catch (CosmosException e)
                    {
                        if (e.StatusCode == HttpStatusCode.PreconditionFailed)
                        {
                            await Task.Delay(this.refreshIntervalS * 1000);
                            continue;
                        }

                        throw;
                    }

                    return token + 1;
                }

                await Task.Delay(this.refreshIntervalS * 1000);
                continue;
            }
        }

        /// <summary>
        /// This function will check to see if the current token is valid.  It is possible that the lease has expired and a new lease needs to be created.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<bool> HasLease(int token)
        {
            try
            {
                LockDocument lockRecord = await lockContainer.ReadItemAsync<LockDocument>(
                        id: lockName,
                        partitionKey: new PartitionKey(lockName)
                    );

                //check to see if we are the owner and the current token is valid
                if (this.ownerId.Equals(lockRecord.Owner) && token == lockRecord.Token)
                {
                    try
                    {
                        lockRecord = await lockContainer.ReadItemAsync<LockDocument>(
                            id: ownerId,
                            partitionKey: new PartitionKey(ownerId)
                        );

                        return true;
                    }
                    catch (CosmosException e)
                    {
                        if (e.StatusCode == HttpStatusCode.NotFound)
                        {

                            try
                            {
                                LockDocument newLockRecord = new LockDocument();
                                newLockRecord.Id = this.lockName;
                                newLockRecord.Owner = "";
                                newLockRecord.Token = token;
                                newLockRecord.Ttl = -1;

                                //ETag is needed just in case another thread got the lock before we did (could happen for any number of reasons)
                                ItemRequestOptions ro = new ItemRequestOptions { IfMatchEtag = lockRecord.ETag };

                                await lockContainer.ReplaceItemAsync<LockDocument>(
                                    item: newLockRecord,
                                    id: newLockRecord.Id,
                                    requestOptions: ro,
                                    partitionKey: new PartitionKey(newLockRecord.Id)
                                );

                                return false;
                            }
                            catch (CosmosException e2)
                            {
                                //someone else updated the item and probably got the lock
                                if (e2.StatusCode == HttpStatusCode.PreconditionFailed)
                                {
                                    return false;
                                }

                                throw;
                            }
                        }

                        throw;
                    }
                }

                return false;
            }
            catch (CosmosException e)
            {
                if (e.StatusCode == HttpStatusCode.NotFound)
                {
                    return false;
                }

                throw;
            }
        }
    }

}
