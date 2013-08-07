using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace SharpInterwiki
{
    struct NamespaceConformity
    {
        public string Lang1;
        public int Ns1;
        public string Lang2;
        public int Ns2;
    }

    class BotConfiguration
    {
        public int MinInterwikiNumber { get; private set; }
        public string CommonLog { get; private set; }
        public string ActionLog { get; private set; }
        public string ConflictLog { get; private set; }
        public int CommonLogLevel { get; private set; }
        public int ActionLogLevel { get; private set; }
        public int ConflictLogLevel { get; private set; }
        public List<NamespaceConformity> NamespaceConformities { get; private set; }

        private readonly Regex _nsRegex = new Regex(@"([a-z\-]*):?(\d+)\|([a-z\-]*):?(\d+)");

        public bool ReadConfiguration(string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                if (!string.IsNullOrEmpty(Settings.Default.BotConfiguration))
                    filename = Settings.Default.BotConfiguration;
                else
                    filename = "BotConfiguration.txt";
            }

            string text;
            try
            {
                using (StreamReader sr = new StreamReader(filename))
                {
                    text = sr.ReadToEnd();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return false;
            }

            var lines = text.Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var ind = line.IndexOf(':');
                if (ind < 0) 
                    continue;
                var varname = line.Remove(ind).Trim();
                var varvalue = line.Substring(ind + 1).Trim();
                int intvalue;

                switch (varname)
                {
                    case "MinInterwikiNumber" :
                        MinInterwikiNumber = int.TryParse(varvalue, out intvalue) ? intvalue : 2;
                        break;
                    case "CommonLog" :
                        CommonLog = varvalue;
                        break;
                    case "ActionLog" :
                        ActionLog = varvalue;
                        break;
                    case "ConflictLog" :
                        ConflictLog = varvalue;
                        break;
                    case "CommonLogLevel" : 
                        ConflictLogLevel = int.TryParse(varvalue, out intvalue) ? intvalue : 0;
                        break;
                    case "ActionLogLevel" :
                        ActionLogLevel = int.TryParse(varvalue, out intvalue) ? intvalue : 0;
                        break;
                    case "ConflictLogLevel" :
                        ConflictLogLevel = int.TryParse(varvalue, out intvalue) ? intvalue : 0;
                        break;
                    default:
                        break;
                }
            }

            if (MinInterwikiNumber < 1)
                MinInterwikiNumber = 1;
            if (string.IsNullOrEmpty(ConflictLog) || string.IsNullOrEmpty(ActionLog) || string.IsNullOrEmpty(CommonLog))
                return false;

            return true;
        }

        public void ReadNamespaceConformity()
        {
            NamespaceConformities = new List<NamespaceConformity>();

            string filename;

            if (!string.IsNullOrEmpty(Settings.Default.BotConfiguration))
                filename = Settings.Default.Namespaces;
            else
                filename = "Namespaces.txt";

            string text;
            try
            {
                using (StreamReader sr = new StreamReader(filename))
                {
                    text = sr.ReadToEnd();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return;
            }

            MatchCollection matches = _nsRegex.Matches(text);
            foreach (Match m in matches)
            {
                var nc = new NamespaceConformity
                    {
                        Lang1 = m.Groups[1].Value,
                        Lang2 = m.Groups[3].Value
                    };
                if (!int.TryParse(m.Groups[2].Value, out nc.Ns1))
                    continue;
                if (!int.TryParse(m.Groups[4].Value, out nc.Ns2))
                    continue;

                NamespaceConformities.Add(nc);
            }
        }

        public bool CheckNamespaceConfirmity(string lang1, int ns1, string lang2, int ns2)
        {
            if (NamespaceConformities.Any(nc =>
                                          (nc.Lang1 == lang1 || nc.Lang1 == "")
                                          && nc.Ns1 == ns1
                                          && (nc.Lang2 == lang2 || nc.Lang2 == "")
                                          && nc.Ns2 == ns2))
                return true;
            if (NamespaceConformities.Any(nc =>
                                          (nc.Lang1 == lang2 || nc.Lang1 == "")
                                          && nc.Ns1 == ns2
                                          && (nc.Lang2 == lang1 || nc.Lang2 == "")
                                          && nc.Ns2 == ns1))
                return true;

            return false;
        }
    }
}
