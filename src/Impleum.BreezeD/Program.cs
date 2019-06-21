using System;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Protocol;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.Api;
using Stratis.Bitcoin.Features.LightWallet;
using Stratis.Bitcoin.Features.Notifications;
using Stratis.Bitcoin.Utilities;

namespace Impleum.BreezeD
{
    public class Program
    {
        public static void Main(string[] args)
        {
            MainAsync(args).Wait();
        }

        public static async Task MainAsync(string[] args)
        {
            try
            {
                // Get the API uri.
                var isTestNet = args.Contains("-testnet");
                var isImpleum = args.Contains("impleum");

                var agent = "Impleum Privacy";

                NodeSettings nodeSettings;

                if (isImpleum)
                {
                    Network network = isTestNet ? Network.ImpleumTest : Network.ImpleumMain;
                    if (isTestNet)
                        args = args.Append("-addnode=94.131.240.45").ToArray(); // TODO: fix this temp hack

                    nodeSettings = new NodeSettings(network, ProtocolVersion.ALT_PROTOCOL_VERSION, agent, args:args, loadConfiguration:false);
                }
                else
                {
                    nodeSettings = new NodeSettings(agent: agent, args: args, loadConfiguration:false);
                }

                IFullNodeBuilder fullNodeBuilder = new FullNodeBuilder()
                    .UseNodeSettings(nodeSettings)
                    .UseLightWallet()
                    .UseBlockNotification()
                    .UseTransactionNotification()
                    .UseApi();

                IFullNode node = fullNodeBuilder.Build();

                // Start Full Node - this will also start the API.
                if (node != null)
                    await node.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("There was a problem initializing the node. Details: '{0}'", ex.Message);
            }
        }
    }
}
