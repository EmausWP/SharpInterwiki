using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Xml;
using SharpWikiApiFunctions;

namespace SharpInterwiki
{
    struct NamespaceConformity
    {
        public string Lang1;
        public int Ns1;
        public string Lang2;
        public int Ns2;
    }

    internal class BotConfiguration
    {
        public int MinInterwikiNumber { get; private set; }
        public int[] Portions { get; private set; }
        public string CommonLog { get; private set; }
        public string ActionLog { get; private set; }
        public string ConflictLog { get; private set; }
        public int CommonLogLevel { get; private set; }
        public int ActionLogLevel { get; private set; }
        public int ConflictLogLevel { get; private set; }
        public Dictionary<string, Dictionary<int, string>> Namespaces { get; private set; }
        public List<NamespaceConformity> NamespaceConformities { get; private set; }
        public List<string> InterwikiOrder { get; private set; }
        public Dictionary<string, List<string>> CategoryBotExclusions { get; private set; }
        public int MoveWaitMin { get; private set; }

        private LanguageCodes _languageCodes;

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

            MoveWaitMin = 60;

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
            var portionsStr = "";

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
                    case "MinInterwikiNumber":
                        MinInterwikiNumber = int.TryParse(varvalue, out intvalue) ? intvalue : 2;
                        break;
                    case "Portions":
                        portionsStr = varvalue;
                        break;
                    case "MoveWaitMin":
                        MoveWaitMin = int.TryParse(varvalue, out intvalue) ? intvalue : 60;
                        break;
                    case "CommonLog":
                        CommonLog = varvalue;
                        break;
                    case "ActionLog":
                        ActionLog = varvalue;
                        break;
                    case "ConflictLog":
                        ConflictLog = varvalue;
                        break;
                    case "CommonLogLevel":
                        ConflictLogLevel = int.TryParse(varvalue, out intvalue) ? intvalue : 0;
                        break;
                    case "ActionLogLevel":
                        ActionLogLevel = int.TryParse(varvalue, out intvalue) ? intvalue : 0;
                        break;
                    case "ConflictLogLevel":
                        ConflictLogLevel = int.TryParse(varvalue, out intvalue) ? intvalue : 0;
                        break;
                    case "InterwikiOrder":
                        InterwikiOrder = GetInterwikiOrder(varvalue);
                        break;
                    default:
                        break;
                }
            }

            ParsePortions(portionsStr);

            if (MinInterwikiNumber < 1)
                MinInterwikiNumber = 1;
            if (string.IsNullOrEmpty(ConflictLog) || string.IsNullOrEmpty(ActionLog) || string.IsNullOrEmpty(CommonLog))
                return false;

            return true;
        }

        private List<string> GetInterwikiOrder(string src)
        {
            return src.Split(new[] {' ', ','}, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private void ParsePortions(string portionsStr)
        {
            var strItems = portionsStr.Split(',');
            var intItems = new List<int>();
            foreach (var item in strItems)
            {
                int pSize;
                if (int.TryParse(item.Trim(), out pSize))
                    intItems.Add(pSize);
            }
            if (intItems.Count > 0)
                Portions = intItems.OrderByDescending(i => i).ToArray();
            else
                Portions = new[] {50, 30, 20, 10, 5, 1};
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

        public bool CheckNamespaceConfirmity(string lang1, string title1, string lang2, string title2)
        {
            if (string.IsNullOrEmpty(title1) || string.IsNullOrEmpty(title2))
                return false;
            int ns1 = GetNamespace(lang1, title1);
            int ns2 = GetNamespace(lang2, title2);

            return CheckNamespaceConfirmity(lang1, ns1, lang2, ns2);
        }

        public bool CheckNamespaceConfirmity(string lang1, int ns1, string lang2, int ns2)
        {
            if (ns1 == ns2)
                return true;
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

        public void ReadNSData()
        {
            string text;

            using (StreamReader sr = new StreamReader(@"G:\Temp\wikipedia_family.py"))
            {
                text = sr.ReadToEnd();
            }

            var blocks = text.Split(new[] {"self.crossnamespace"}, StringSplitOptions.RemoveEmptyEntries);
            foreach (var block in blocks)
            {
                var ind = block.IndexOf("]");
                if (ind < 0)
                    continue;
                var fns = block.Remove(ind);
                fns = fns.Substring(fns.LastIndexOf("[") + 1).Trim();

                var block1 = block.Substring(block.IndexOf("{", ind) + 1);
                var items = block1.Split(new[] {"},"}, StringSplitOptions.RemoveEmptyEntries);

                foreach (var item in items)
                {
                    ind = item.IndexOf(":");
                    if (ind < 0)
                        continue;
                    var fcode = item.Remove(ind).Trim().Trim('\'');
                    var cdis = item.Substring(ind + 1)
                                   .Trim()
                                   .Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var cdi in cdis)
                    {
                        ind = cdi.IndexOf(":");
                        if (ind < 0)
                            continue;
                        var scode = cdi.Remove(ind).Trim().Trim('\'');
                        var snses = cdi.Substring(ind + 1).Trim().Trim(',').TrimStart('[').TrimEnd(']').Split(',');
                        foreach (var sns in snses)
                        {
                            int snsint, fsnsint, rem;
                            if (!int.TryParse(sns, out snsint) || ! int.TryParse(fns, out fsnsint))
                                continue;
                            Math.DivRem(snsint, 2, out rem);
                            if (rem == 1)
                                continue;
                            if (snsint <= fsnsint)
                                continue;
                            using (StreamWriter sw = new StreamWriter(@"G:\Temp\wikipedia_family_parsed.txt", true))
                            {
                                sw.WriteLine("{0}:{1}|{2}:{3}", fcode, fns, scode, sns.Trim());
                            }
                        }
                    }
                }
            }
        }

        public void GetNamespaces(string code, string project)
        {
            if (Namespaces == null)
                Namespaces = new Dictionary<string, Dictionary<int, string>>();
            if (Namespaces.ContainsKey(code))
                return;
            Namespaces.Add(code, WikiApiFunctions.GetNamespaces(code, project));
        }

        public List<PageInfo> FilterByNamespace(List<PageInfo> pages, string code, int ns)
        {
            if (ns == -100)
                return pages.Where(p => IsEvenNamespace(code, p.Title)).ToList();
            return pages.Where(p => GetNamespace(code, p.Title) == ns).ToList();
        }

        public List<string> FilterByNamespace(List<string> pages, string code, int ns)
        {
            return pages.Where(p => GetNamespace(code, p) == ns).ToList();
        }

        private bool IsEvenNamespace(string code, string title)
        {
            var ns = GetNamespace(code, title);
            int rem;
            Math.DivRem(ns, 2, out rem);
            return rem == 0;
        }

        public int GetNamespace(string code, string title)
        {
            if (string.IsNullOrEmpty(title))
                return -10;
            foreach (var ns in Namespaces[code])
            {
                if (title.StartsWith(ns.Value + ":"))
                    return ns.Key;
            }

            return 0;
        }

        public LanguageCodes GetWikiCodes()
        {
            _languageCodes = new LanguageCodes();
            _languageCodes.GetWikiCodes();
            return _languageCodes;
        }

        public LanguageCodes GetWikiCodes(string project)
        {
            _languageCodes = new LanguageCodes(project);
            _languageCodes.GetWikiCodes();
            return _languageCodes;
        }

        public List<string> GetCategoryBotExclusions(string langcode)
        {
            CategoryBotExclusions = new Dictionary<string, List<string>>();

            try
            {
                using (StreamReader sr = new StreamReader("CategoryBotExclusions.txt"))
                {
                    while (sr.Peek() > 0)
                    {
                        var newline = sr.ReadLine();
                        if (newline == null)
                            continue;
                        var pos = newline.IndexOf(":");
                        if (pos < 0)
                            continue;
                        var code = newline.Substring(0, pos).Trim();
                        var user = newline.Substring(pos + 1).Trim();
                        if (code.Length == 0 || user.Length == 0)
                            continue;
                        if (!CategoryBotExclusions.ContainsKey(code))
                            CategoryBotExclusions.Add(code, new List<string>());
                        CategoryBotExclusions[code].Add(user);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            if(CategoryBotExclusions.ContainsKey(langcode))
                return CategoryBotExclusions[langcode];
            else
                return new List<string>();
            
        }
    }
}
