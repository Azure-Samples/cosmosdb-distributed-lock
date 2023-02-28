using Microsoft.Azure.Cosmos;

namespace Cosmos_Patterns_GlobalLock
{
    public class LockTest
    {
        public CosmosClient client;

        public const string LockDB = "LockDB";
        public const string LockCollection = "Locks";

        private readonly string lockName;

        private int lockDuraction;

        private volatile int globalToken;
        public volatile bool isActive = true;

        public LockTest(CosmosClient client, string lockName, int lockDuration)
        {
            this.client = client;
            this.lockName = lockName;
            this.lockDuraction = lockDuration;
        }

        public async Task StartThread()
        {
            var mutex = await Lock.CreateLock(client, LockDB, LockCollection, lockName, 1);

            while (this.isActive)
            {
                var seenToken = this.globalToken;

                var localToken = await mutex.AcquireLease(lockDuraction);

                Console.WriteLine($"{DateTime.Now}]: {mutex.ownerId}: got lock token = {localToken}");

                if (localToken <= seenToken)
                {
                    throw new Exception($"[{DateTime.Now}]: {mutex.ownerId} : Violation: {localToken} was acquired after {seenToken} was seen");
                }

                this.globalToken = localToken;

                while (true)
                {
                    seenToken = this.globalToken;

                    if (seenToken > localToken)
                    {
                        Console.WriteLine($"{DateTime.Now}]: {mutex.ownerId}: expect to lose {localToken} lease because {seenToken} was seen");
                    }

                    if (await mutex.HasLease(localToken))
                    {
                        if (seenToken > localToken)
                        {
                            throw new Exception($"{DateTime.Now}]: Violation: lease to {localToken} was confirmed after {seenToken} was seen");
                        }

                        Console.WriteLine($"{DateTime.Now}]: {mutex.ownerId}: has lock token = {localToken}");

                        await Task.Delay(500);
                    }
                    else
                    {
                        Console.WriteLine($"{DateTime.Now}]: {mutex.ownerId}: lost lock");
                        break;
                    }
                }
            }
        }
    }

}
