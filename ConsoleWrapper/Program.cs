using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleWrapper
{
    class Program
    {
        private const int STD_INPUT_HANDLE = -10;
        private const uint ENABLE_QUICK_EDIT = 0x0040;
        private const uint ENABLE_EXTENDED_FLAGS = 0x0080;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

        [DllImport("user32.dll")]
        private static extern bool EnableMenuItem(IntPtr hMenu, uint uIDEnableItem, uint uEnable);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int MessageBox(IntPtr hWnd, String text, String caption, uint type);

        private const int SW_HIDE = 0;
        private const uint SC_CLOSE = 0xF060;
        private const uint MF_GRAYED = 0x1;
        private const uint MB_OK = 0x00000000;

        static async Task Main(string[] args)
        {
            if (Properties.Settings.Default.HideAppWrapperConsole)
            {
                // Hide the wrapper console window
                var handle = GetConsoleWindow();
                ShowWindow(handle, SW_HIDE);
            }
            // Disable QuickEdit mode
            DisableQuickEditMode();
            // Disable the close button
            DisableCloseButton();
            // Start the stopwatch
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            // Set up the process start info
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = Properties.Settings.Default.WrapApplication; // Replace with the path to your executable
            startInfo.Arguments = string.Join(" ", args);
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;

            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.EnableRaisingEvents = true;

                // Set up asynchronous reading of the output
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine(e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.Error.WriteLine(e.Data);
                    }
                };

                // Handle the process exit event
                var processExitedTcs = new TaskCompletionSource<bool>();
                process.Exited += (sender, e) => processExitedTcs.SetResult(true);

                // Start the process
                process.Start();

                // Begin reading the output asynchronously
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Handle input asynchronously
                var inputTask = Task.Run(() =>
                {
                    using (StreamWriter inputWriter = process.StandardInput)
                    {
                        string inputLine;
                        while ((inputLine = Console.ReadLine()) != null)
                        {
                            inputWriter.WriteLine(inputLine);
                        }
                    }
                });

                // Wait for the process to exit and for the input task to complete
                await processExitedTcs.Task;
                if (Properties.Settings.Default.AwaitForInputToEndOrCtrlC)
                    await inputTask;

                // Stop the stopwatch
                stopwatch.Stop();

                // Display the elapsed time
                Console.WriteLine($"Execution time: {stopwatch.Elapsed}");

                if (Properties.Settings.Default.DbgMsgEnd)
                // Show a message box on a separate thread
                new Thread(() =>
                {
                    MessageBox(IntPtr.Zero, $"The application has finished running successfully. Execution time: {stopwatch.Elapsed} ms", "Success", MB_OK);
                }).Start();
            }
        }

        private static void DisableQuickEditMode()
        {
            IntPtr consoleHandle = GetStdHandle(STD_INPUT_HANDLE);
            uint consoleMode;
            GetConsoleMode(consoleHandle, out consoleMode);
            consoleMode &= ~ENABLE_QUICK_EDIT;
            consoleMode |= ENABLE_EXTENDED_FLAGS;
            SetConsoleMode(consoleHandle, consoleMode);
        }

        private static void DisableCloseButton()
        {
            IntPtr consoleWindow = GetConsoleWindow();
            IntPtr hMenu = GetSystemMenu(consoleWindow, false);
            EnableMenuItem(hMenu, SC_CLOSE, MF_GRAYED);
        }
    }
}
