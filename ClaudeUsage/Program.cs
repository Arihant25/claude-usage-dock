using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ClaudeUsage;

public class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            global::Shmuelie.WinRTServer.ComServer server = new();

            ManualResetEvent extensionDisposedEvent = new(false);
            
            // We are instantiating an extension instance once above, and returning it every time the callback in RegisterExtension below is called.
            // This makes sure that only one instance of SampleExtension is alive, which is returned every time the host asks for the IExtension object.
            // If you want to instantiate a new instance each time the host asks, create the new instance inside the delegate.
            ClaudeUsageExtension extensionInstance = new(extensionDisposedEvent);
            server.RegisterClass<ClaudeUsageExtension, IExtension>(() => extensionInstance);
            server.Start();
            
            // This will make the main thread wait until the event is signalled by the extension class.
            // Since we have single instance of the extension object, we exit as soon as it is disposed.
            extensionDisposedEvent.WaitOne();
            server.Stop();
            server.UnsafeDispose();
        }
        else
        {
            if (args.Length > 0 && args[0] is "--connect" or "--disconnect")
            {
                // Scriptable setup: claude-usage-bridge.exe --connect | --disconnect
                try
                {
                    Console.WriteLine(args[0] == "--connect" ? ClaudeSettingsConnector.Connect() : ClaudeSettingsConnector.Disconnect());
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    Environment.Exit(1);
                }
            }

            // Invoked by Claude Code through the claude-usage-bridge.exe alias.
            Environment.Exit(StatusLineBridge.Run(args));
        }
    }
}
