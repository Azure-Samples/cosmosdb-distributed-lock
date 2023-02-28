using Microsoft.Azure.Cosmos;
using System.Net;

namespace Cosmos_Patterns_GlobalLock
{
    internal class Program
    {
        static CosmosClient? client;

        static Database? db;

        static Container? lockContainer;

        static string partitionKeyPath = "/id";

        static string databaseName = "LockDB";

        static async Task Main(string[] args)
        {
            client = new(
                accountEndpoint: Environment.GetEnvironmentVariable("COSMOS_ENDPOINT")!,
                authKeyOrResourceToken: Environment.GetEnvironmentVariable("COSMOS_KEY")!);

            db = await client.CreateDatabaseIfNotExistsAsync(
                id: databaseName
            );

            lockContainer = await db.CreateContainerIfNotExistsAsync(
                id: "Locks",
                partitionKeyPath: "/id",
                throughput: 400
            );

            await MainAsync();
        }

        /// <summary>
        /// This function runs two threads that attempt to compete for a lock.  Only one thread can have the lock on an object at a time.
        /// </summary>
        /// <returns></returns>
        static async Task MainAsync()
        {
            Console.WriteLine("Running complex lease example...");

            string lockName = "lock1";
            
            //in seconds
            int lockDuration = 30;
            
            Console.WriteLine("Enter the name of the lock:");
            lockName = Console.ReadLine();

            Console.WriteLine("Enter the lock duration in seconds:");
            try
            {
                lockDuration = int.Parse(Console.ReadLine());
            }
            catch
            {

            }

            var test = new LockTest(client, lockName, lockDuration);

            var tasks = new List<Task>();

            Console.WriteLine("Starting two threads...");
            tasks.Add(test.StartThread());
            tasks.Add(test.StartThread());

            //run for 30 seconds...
            await Task.Delay(30 * 1000);

            Console.WriteLine("Disabling threads...");
            //tell all threads to stop
            test.isActive = false;

            //wait for them to finish
            await Task.WhenAll(tasks);

            Console.WriteLine("Distributed locks works as designed, hit enter to exit");
            Console.ReadLine();
        }
    }
}