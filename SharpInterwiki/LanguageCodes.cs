using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using SharpWikiApiFunctions;

namespace SharpInterwiki
{
    class LanguageCodes
    {
        public Dictionary<string, Dictionary<string, string>> WikiCodes { get; private set; }
        private string _currentProject;
        private int _daysBeforeUpdate = 10;

        public LanguageCodes()
        {
        }

        public LanguageCodes(string project)
        {
            _currentProject = project;
        }

        public void SetProjectCode(string project)
        {
            _currentProject = project;
        }

        public Dictionary<string, Dictionary<string, string>> GetWikiCodes()
        {
            if (WikiCodes != null && WikiCodes.ContainsKey("wikipedia") && WikiCodes["wikipedia"].Count > 10)
                return WikiCodes;

            string filename = "Cache" + Path.DirectorySeparatorChar + "SiteMatrix.gz";

            var fileExists = File.Exists(filename);
            DateTime lastFileUpdate = new DateTime();
            if (fileExists)
                lastFileUpdate = File.GetLastWriteTimeUtc(filename);
            var readFromFile = true;
            if (!fileExists || DateTime.UtcNow.Subtract(lastFileUpdate).Days > _daysBeforeUpdate)
            {
                readFromFile = !GetWikiCodesFromSite("meta.wikimedia.org");
                if (readFromFile)
                    readFromFile = !GetWikiCodesFromSite("www.wikidata.org");
                if (readFromFile)
                    readFromFile = !GetWikiCodesFromSite("en.wikipedia.org");
                if (!readFromFile)
                    WriteWikiCodesToFile(filename);
            }
            if (readFromFile && fileExists)
            {
                ReadWikiCodesFromFile(filename);
            }

            return WikiCodes;
        }

        private bool GetWikiCodesFromSite(string projectUrl)
        {
            for (int i = 0; i < 5; i++)
            {
                var wikiCodes = WikiApiFunctions.GetWikiCodes(projectUrl);
                if (wikiCodes.ContainsKey("wikipedia") && wikiCodes["wikipedia"].Count > 10)
                {
                    WikiCodes = wikiCodes;
                    return true;
                }
                System.Threading.Thread.Sleep(i * 1000);
            }
            return false;
        }

        private bool ReadWikiCodesFromFile(string filename)
        {
            var wikiCodes = new Dictionary<string, Dictionary<string, string>>();
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    string xmlText;
                    using (FileStream fs = new FileStream(filename, FileMode.Open))
                    using (GZipStream gs = new GZipStream(fs, CompressionMode.Decompress))
                    using (TextReader sr = new StreamReader(gs))
                    {
                        xmlText = sr.ReadToEnd();
                    }
                    var doc = new XmlDocument();
                    doc.LoadXml(xmlText);
                    var projectNodes = doc.SelectNodes("//project");
                    if (projectNodes == null)
                        return false;
                    foreach (XmlNode projectNode in projectNodes)
                    {
                        if (projectNode.Attributes == null)
                            continue;
                        var projectName = projectNode.Attributes["name"].Value.Trim();
                        if (string.IsNullOrEmpty(projectName))
                            continue;
                        var subProjectDictionary = new Dictionary<string, string>();
                        var subProjectNodes = projectNode.SelectNodes("./subproject");
                        foreach (XmlNode subProjectNode in subProjectNodes)
                        {
                            if (subProjectNode.Attributes == null)
                                continue;
                            var key = subProjectNode.Attributes["key"].Value;
                            var value = subProjectNode.Attributes["value"].Value;
                            if (!string.IsNullOrEmpty(key) &&
                                !string.IsNullOrEmpty(value) &&
                                !subProjectDictionary.ContainsKey(key))
                                subProjectDictionary.Add(key, value);
                        }
                        if (!wikiCodes.ContainsKey(projectName))
                            wikiCodes.Add(projectName, subProjectDictionary);
                    }
                    break;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    if (i < 4 && e is IOException)
                        System.Threading.Thread.Sleep(5000);
                    else
                        return false;
                }
            }

            if (wikiCodes.ContainsKey("wikipedia") && wikiCodes["wikipedia"].Count > 10)
            {
                WikiCodes = wikiCodes;
                return true;
            }

            return false;
        }

        private void WriteWikiCodesToFile(string filename)
        {
            var text = "<projects>\n";
            foreach (var wikiCode in WikiCodes)
            {
                text += string.Format(" <project name=\"{0}\">\n", wikiCode.Key);
                foreach (var kv in wikiCode.Value)
                {
                    text += string.Format("  <subproject key=\"{0}\" value=\"{1}\"/>\n", kv.Key, kv.Value);
                }
                text += "</project>\n";
            }
            text += "</projects>";

            using (FileStream fs = new FileStream(filename, FileMode.Create))
            using (GZipStream gs = new GZipStream(fs, CompressionMode.Compress))
            using (StreamWriter sw = new StreamWriter(gs))
            {
                sw.Write(text);
            }
        }

        public string ToProjectCode(string languageCode)
        {
            return ToProjectCode(languageCode, _currentProject);
        }

        public string ToProjectCode(string languageCode, string project)
        {
            if (!WikiCodes.ContainsKey(project)
                || !WikiCodes[project].ContainsValue(languageCode))
                return "";
            return WikiCodes[project].First(c => c.Value == languageCode).Key;
        }

        public Dictionary<string, string> ToProjectCodes(Dictionary<string, string> dic)
        {
            return ToProjectCodes(dic, _currentProject);
        }

        public Dictionary<string, string> ToProjectCodes(Dictionary<string, string> dic, string project)
        {
            var projectDic = new Dictionary<string, string>();
            if (!WikiCodes.ContainsKey(project))
                return projectDic;
            foreach (var kv in dic)
            {
                if(!WikiCodes[project].ContainsValue(kv.Key))
                    continue;
                var projectCode = WikiCodes[project].First(c => c.Value == kv.Key).Key;
                projectDic.Add(projectCode, kv.Value);
            }

            return projectDic;
        }

        public string ToLanguageCode(string projectCode)
        {
            return ToLanguageCode(projectCode, _currentProject);
        }

        public string ToLanguageCode(string projectCode, string project)
        {
            if (!WikiCodes.ContainsKey(project)
                || !WikiCodes[project].ContainsKey(projectCode))
                return "";
            return WikiCodes[project][projectCode];
        }

        public Dictionary<string, string> ToLanguageCodes(Dictionary<string, string> dic)
        {
            return ToLanguageCodes(dic, _currentProject);
        }

        public Dictionary<string, string> ToLanguageCodes(Dictionary<string, string> dic, string project)
        {
            var projectDic = new Dictionary<string, string>();
            if (!WikiCodes.ContainsKey(project))
                return projectDic;
            foreach (var kv in dic.Where(kv => WikiCodes[project].ContainsKey(kv.Key)))
            {
                projectDic.Add(WikiCodes[project][kv.Key], kv.Value);
            }

            return projectDic;
        }

        public bool ContainsLanguageCode(string language)
        {
            return ContainsLanguageCode(language, _currentProject);
        }

        public bool ContainsLanguageCode(string language, string project)
        {
            return WikiCodes.ContainsKey(project) && WikiCodes[project].ContainsValue(language);
        }

        public Dictionary<string, string> MakeAddList(Dictionary<string, string> oldList,
                                                           Dictionary<string, string> newList)
        {
            return newList
                .Where(kv => !oldList.ContainsKey(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        public Dictionary<string, KeyValuePair<string, string>> MakeReplaceList(Dictionary<string, string> oldList,
                                                           Dictionary<string, string> newList)
        {
            return newList
                .Where(kv => oldList.ContainsKey(kv.Key) && oldList[kv.Key] != kv.Value)
                .ToDictionary(kv => kv.Key, kv => new KeyValuePair<string, string>(oldList[kv.Key], kv.Value));
        }

        public string GetShortWikiProjectCode(string project)
        {
            switch (project.ToLower())
            {
                case "wiki":
                    return "";
                case "wikipedia":
                    return "";
                case "wikisource":
                    return "s";
                case "wikinews":
                    return "n";
                case "wikibooks":
                    return "b";
                case "wikiquote":
                    return "q";
                case "wikiversity":
                    return "v";
                case "wikiktionary":
                    return "wikt";
                case "wikivoyage":
                    return "voy";
                default:
                    return project;
            }
        }

        public string MakeWikiTextLink(string language, string title, bool makeLong)
        {
            return MakeWikiTextLink(language, _currentProject, title, makeLong);
        }

        public string MakeWikiTextLink(string language, string project, string title, bool makeLong)
        {
            string shortWikiProjectCode = GetShortWikiProjectCode(project);
            string wikiTextLink = language;
            if (shortWikiProjectCode.Length > 0)
                wikiTextLink += ":" + shortWikiProjectCode;
            if (makeLong)
            {
                wikiTextLink = "[[" + wikiTextLink + ":" + title + "]]";
            }
            
            return wikiTextLink;
        }
    }
}
