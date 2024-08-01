using Newtonsoft.Json;
using Sharlayan;
using Sharlayan.Core;
using Sharlayan.Extensions;
using Sharlayan.Models;
using Sharlayan.Models.ReadResults;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SharlayanReader
{
    internal class Program
    {
        static bool isRunning = false;

        static int _previousArrayIndex = 0;
        static int _previousOffset = 0;

        static string lastDialogText = "";
        static List<ChatLogItem> lastChatLogEntries = new List<ChatLogItem>();
        static string lastCutsceneText = "";

        static readonly List<string> systemCode = new List<string>() { "0039", "0839", "0003", "0038", "003C", "0048", "001D", "001C" };

        static void Main() //static void Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
            while (true)
            {
                try
                {
                    MemoryHandler memoryHandler = CreateMemoryHandler();
                    RunReader(memoryHandler);
                }
                catch (Exception ex)
                {
                    SystemFunctions.WriteConsoleMessage(ex.Message);
                }
                SystemFunctions.TaskDelay(1000);
            }
        }

        static MemoryHandler CreateMemoryHandler()
        {
            // Get process
            Process[] processes = Process.GetProcessesByName("ffxiv_dx11");
            if (processes.Length <= 0) { throw new Exception("Waiting..."); }

            // Create configuration
            SharlayanConfiguration configuration = new SharlayanConfiguration
            {
                ProcessModel = new ProcessModel
                {
                    Process = processes.FirstOrDefault(),
                },
            };

            // Create memoryHandler
            MemoryHandler memoryHandler = new MemoryHandler(configuration);
            memoryHandler.Scanner.Locations.Clear();

            // Set signatures
            string signaturesText = File.ReadAllText("signatures.json");
            var signatures = JsonConvert.DeserializeObject<List<Signature>>(signaturesText);
            if (signatures != null)
            {
                memoryHandler.Scanner.LoadOffsets(signatures.ToArray());
            }

            return memoryHandler;
        }

        static void RunReader(MemoryHandler memoryHandler)
        {
            SystemFunctions.WriteConsoleMessage("Start reading...");

            isRunning = true;
            Task.Run(AliveCheck);

            while (isRunning)
            {
                if (!memoryHandler.Scanner.IsScanning)
                {
                    ReadDialog(memoryHandler);
                    ReadChatLog(memoryHandler);
                    ReadCutscene(memoryHandler);
                }

                SystemFunctions.TaskDelay();
            }

            SystemFunctions.WriteConsoleMessage("Stop reading...");
        }

        static async Task AliveCheck()
        {
            while (true)
            {
                Process[] processes = Process.GetProcessesByName("ffxiv_dx11");
                if (processes.Length > 0)
                {
                    isRunning = true;
                    await Task.Delay(1000);
                }
                else
                {
                    isRunning = false;
                    break;
                }
            }
        }

        static void ReadDialog(MemoryHandler memoryHandler)
        {
            try
            {
                string dialogName = StringFunctions.GetMemoryString(memoryHandler, "PANEL_NAME", 128);
                string dialogText = StringFunctions.GetMemoryString(memoryHandler, "PANEL_TEXT", 512);

                if (dialogName.Length > 0 && dialogText.Length > 0 && dialogText != lastDialogText)
                {
                    lastDialogText = dialogText;
                    SystemFunctions.WriteData("DIALOG", "003D", dialogName, dialogText);
                }
            }
            catch (Exception)
            {
            }
        }

        static void ReadChatLog(MemoryHandler memoryHandler)
        {
            try
            {
                ChatLogResult readResult = memoryHandler.Reader.GetChatLog(_previousArrayIndex, _previousOffset);
                List<ChatLogItem> chatLogEntries = readResult.ChatLogItems.ToList();

                _previousArrayIndex = readResult.PreviousArrayIndex;
                _previousOffset = readResult.PreviousOffset;

                if (chatLogEntries.Count > 0)
                {
                    if (ArrayFunctions.IsSameChatLogEntries(chatLogEntries, lastChatLogEntries))
                    {
                        return;
                    }
                    else
                    {
                        lastChatLogEntries = chatLogEntries;
                    }

                    for (int i = 0; i < chatLogEntries.Count; i++)
                    {
                        ChatLogItem chatLogItem = chatLogEntries[i];

                        string logName = StringFunctions.GetLogName(chatLogItem);
                        string logText = chatLogItem.Message;

                        if (logName.Length == 0 && systemCode.IndexOf(chatLogItem.Code) < 0)
                        {
                            string[] splitedMessage = logText.Split(':');
                            if (splitedMessage[0].Length > 0 && splitedMessage.Length > 1)
                            {
                                logName = splitedMessage[0];
                                logText = logText.Replace(logName + ":", "");
                            }
                        }

                        SystemFunctions.WriteData("CHAT_LOG", chatLogItem.Code, logName, logText);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        static void ReadCutscene(MemoryHandler memoryHandler)
        {
            try
            {
                var cutsceneDetectorPointer = (IntPtr)memoryHandler.Scanner.Locations["CUTSCENE_DETECTOR"];
                int isCutscene = (int)memoryHandler.GetInt64(cutsceneDetectorPointer);

                if (isCutscene == 1) return;

                string cutsceneText = StringFunctions.GetMemoryString(memoryHandler, "CUTSCENE_TEXT", 256);

                if (cutsceneText.Length > 0 && cutsceneText != lastCutsceneText)
                {
                    lastCutsceneText = cutsceneText;
                    SystemFunctions.WriteData("CUTSCENE", "003D", "", cutsceneText);
                }
            }
            catch (Exception)
            {
            }
        }
    }

    class SystemFunctions
    {
        static string lastConsoleText = "";

        public static void WriteConsoleMessage(string text)
        {
            if (text == lastConsoleText) return;
            lastConsoleText = text;
            WriteData("CONSOLE", "FFFF", "", text);
        }

        public static void TaskDelay(int delayTIme = 50)
        {
            try
            {
                Task.Delay(delayTIme).Wait();
            }
            catch (Exception)
            {
            }
        }

        public static async void WriteData(string type, string code, string name, string text, int sleepTime = 0)
        {
            await Task.Delay(sleepTime);

            string dataString = JsonConvert.SerializeObject(new
            {
                type,
                code,
                name,
                text,
            });

            Console.Write(dataString + "\r\n");
        }
    }

    class ArrayFunctions
    {
        public static bool IsSameChatLogEntries(List<ChatLogItem> chatLogEntries, List<ChatLogItem> lastChatLogEntries)
        {
            if (chatLogEntries.Count != lastChatLogEntries.Count)
            {
                return false;
            }

            for (int i = 0; i < chatLogEntries.Count; i++)
            {
                ChatLogItem chatLogItem = chatLogEntries[i];
                ChatLogItem lastChatLogItem = lastChatLogEntries[i];

                if (chatLogItem.Message != lastChatLogItem.Message)
                {
                    return false;
                }
            }

            return true;
        }
    }

    class StringFunctions
    {
        public static string GetLogName(ChatLogItem chatLogItem)
        {
            string logName = "";

            try
            {
                if (chatLogItem.PlayerName != null)
                {
                    logName = chatLogItem.PlayerName;
                }
            }
            catch (Exception)
            {
            }

            return logName;
        }

        public static string GetMemoryString(MemoryHandler memoryHandler, string key, int length)
        {
            string byteString = "";

            try
            {
                byte[] byteArray = memoryHandler.GetByteArray(memoryHandler.Scanner.Locations[key], length);
                byteArray = GetRealByteArray(byteArray);
                byteString = ChatCleaner.ProcessFullLine(byteArray);
            }
            catch (Exception)
            {
            }

            return byteString;
        }

        public static byte[] GetRealByteArray(byte[] byteArray)
        {
            List<byte> byteList = new List<byte>();
            int nullIndex = byteArray.ToList().IndexOf(0x00);

            for (int i = 0; i < nullIndex; i++)
            {
                byteList.Add(byteArray[i]);
            }

            return byteList.ToArray();
        }
    }

    class ChatCleaner
    {
        private const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.ExplicitCapture;

        //private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        //private static readonly Regex PlayerChatCodesRegex = new Regex(@"^00(0[A-F]|1[0-9A-F])$", DefaultOptions);

        private static readonly Regex PlayerRegEx = new Regex(@"(?<full>\[[A-Z0-9]{10}(?<first>[A-Z0-9]{3,})20(?<last>[A-Z0-9]{3,})\](?<short>[\w']+\.? [\w']+\.?)\[[A-Z0-9]{12}\])", DefaultOptions);

        private static readonly Regex ArrowRegex = new Regex(@"\uE06F", RegexOptions.Compiled);

        private static readonly Regex HQRegex = new Regex(@"\uE03C", RegexOptions.Compiled);

        //private static readonly Regex NewLineRegex = new Regex(@"[\r\n]+", RegexOptions.Compiled);
        private static readonly Regex NewLineRegex = new Regex(@"[\n]+", RegexOptions.Compiled);

        //private static readonly Regex NoPrintingCharactersRegex = new Regex(@"[\x00-\x1F\x7F]+", RegexOptions.Compiled);
        private static readonly Regex NoPrintingCharactersRegex = new Regex(@"[\x00-\x0C\x0E-\x1F\x7F]+", RegexOptions.Compiled);

        private static readonly Regex SpecialPurposeUnicodeRegex = new Regex(@"[\uE000-\uF8FF]", RegexOptions.Compiled);

        private static readonly Regex SpecialReplacementRegex = new Regex(@"[\uFFFD]", RegexOptions.Compiled);

        public static string ProcessFullLine(byte[] bytes)
        {
            var line = Encoding.UTF8.GetString(bytes.ToArray()).Replace("  ", " ");
            try
            {
                List<byte> autoTranslateList = new List<byte>(bytes.Length);

                List<byte> newList = new List<byte>();

                for (var x = 0; x < bytes.Count(); x++)
                {
                    switch (bytes[x])
                    {
                        case 2:
                            // special in-game replacements/wrappers
                            // 2 46 5 7 242 2 210 3
                            // 2 29 1 3
                            // remove them
                            var length = bytes[x + 2];
                            var limit = length - 1;
                            if (length > 1)
                            {
                                x = x + 3;

                                ///////////////////////////
                                autoTranslateList.Add(Convert.ToByte('['));
                                byte[] translated = new byte[limit];
                                Buffer.BlockCopy(bytes, x, translated, 0, limit);
                                foreach (var b in translated)
                                {
                                    autoTranslateList.AddRange(Encoding.UTF8.GetBytes(b.ToString("X2")));
                                }

                                autoTranslateList.Add(Convert.ToByte(']'));

                                var bCheckStr = Encoding.UTF8.GetString(autoTranslateList.ToArray());

                                if (bCheckStr != null && bCheckStr.Length > 0)
                                {
                                    if (bCheckStr.Equals("[59]"))
                                    {
                                        newList.Add(0x40);
                                    }

                                    /*
                                    if (Utilities.AutoTranslate.EnDict.TryGetValue(bCheckStr.Replace("[0", "[").ToLower(), out var AutoTranslateVal))
                                    {
                                        newList.AddRange(Encoding.UTF8.GetBytes(AutoTranslateVal));
                                    }
                                    */
                                }
                                autoTranslateList.Clear();
                                ///////////////////////////

                                x += limit;
                            }
                            else
                            {
                                x = x + 4;
                                newList.Add(32);
                                newList.Add(bytes[x]);
                            }

                            break;

                        // unit separator
                        case 31:
                            // TODO: this breaks in some areas like NOVICE chat
                            // if (PlayerChatCodesRegex.IsMatch(code)) {
                            //     newList.Add(58);
                            // }
                            // else {
                            //     newList.Add(31);
                            // }
                            newList.Add(58);
                            /*
                            if (PlayerChatCodesRegex.IsMatch(code))
                            {
                                newList.Add(32);
                            }
                            */
                            break;
                        default:
                            newList.Add(bytes[x]);
                            break;
                    }
                }

                var cleaned = Encoding.UTF8.GetString(newList.ToArray()).Replace("  ", " ");
                newList.Clear();

                // replace right arrow in chat (parsing)
                cleaned = ArrowRegex.Replace(cleaned, "⇒");
                // replace HQ symbol
                cleaned = HQRegex.Replace(cleaned, "[HQ]");
                // replace all Extended special purpose unicode with empty string
                cleaned = SpecialPurposeUnicodeRegex.Replace(cleaned, string.Empty);
                // cleanup special replacement character bytes: 239 191 189
                cleaned = SpecialReplacementRegex.Replace(cleaned, string.Empty);
                // remove new lines
                cleaned = NewLineRegex.Replace(cleaned, string.Empty);
                // remove characters 0-31
                cleaned = NoPrintingCharactersRegex.Replace(cleaned, string.Empty);

                line = cleaned;
            }
            catch (Exception)
            {
                //MemoryHandler.Instance.RaiseException(Logger, ex, true);
                //Console.WriteLine(ex.Message);
            }

            if (line.Contains("�"))
            {
                return "";
            }

            return ProcessName(line);
        }

        private static string ProcessName(string cleaned)
        {
            var line = cleaned;
            try
            {
                // cleanup name if using other settings
                Match playerMatch = PlayerRegEx.Match(line);
                if (playerMatch.Success)
                {
                    var fullName = playerMatch.Groups[1].Value;
                    var firstName = playerMatch.Groups[2].Value.FromHex();
                    var lastName = playerMatch.Groups[3].Value.FromHex();
                    var player = $"{firstName} {lastName}";

                    // remove double placement
                    cleaned = line.Replace($"{fullName}:{fullName}", "•name•");

                    // remove single placement
                    cleaned = cleaned.Replace(fullName, "•name•");
                    switch (Regex.IsMatch(cleaned, @"^([Vv]ous|[Dd]u|[Yy]ou)"))
                    {
                        case true:
                            cleaned = cleaned.Substring(1).Replace("•name•", string.Empty);
                            break;
                        case false:
                            cleaned = cleaned.Replace("•name•", player);
                            break;
                    }
                }

                cleaned = NewLineRegex.Replace(cleaned, string.Empty);
                cleaned = NoPrintingCharactersRegex.Replace(cleaned, string.Empty);
                line = cleaned;
            }
            catch (Exception)
            {
                //MemoryHandler.Instance.RaiseException(Logger, ex, true);
                //Console.WriteLine(ex.Message);
            }

            return line;
        }
    }
}