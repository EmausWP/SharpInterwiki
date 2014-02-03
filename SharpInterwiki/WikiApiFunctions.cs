using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using System.Xml;

namespace SharpWikiApiFunctions
{
    public class PageInfo
    {
        public string Title = "";
        public DateTime Timestamp = new DateTime(1900, 1, 1);
        public string User;
        public string Comment;
        public int Namespace = -1;
        public string NormalizedTitle = "";
        public string RedirectTo = "";
        public string RedirectionSection = "";
        public bool IsMissing = false;
    }

    public class WikiApiException : ApplicationException
    {
        public WikiApiException(string message)
            : base(message)
        {
        }
    }

    public class WikiApiFunctions
    {
        #region Common processing functions

        public static string WebPageDownload(string uri)
        {
            WebClient client = new WebClient();
            client.Headers.Add("user-agent", "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; .NET CLR 1.0.3705;)");
            client.Headers.Add("Accept-Encoding: gzip, deflate");

            Stream data = client.OpenRead(uri);
            var contentEncoding = client.ResponseHeaders["Content-Encoding"];
            if (contentEncoding.ToLower().Contains("gzip"))
                data = new GZipStream(data, CompressionMode.Decompress);
            else if (contentEncoding.ToLower().Contains("deflate"))
                data = new DeflateStream(data, CompressionMode.Decompress);
            StreamReader reader = new StreamReader(data);
            string text = reader.ReadToEnd();
            data.Close();
            reader.Close();

            return text;
        }

        public static string TryWebPageDownload(string uri)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    string text = WebPageDownload(uri);
                    return text;
                }
                catch (IOException e)
                {
                    Console.WriteLine(e);
                    if (i < 5)
                        System.Threading.Thread.Sleep(5000 * i);
                    else
                        throw;
                }
            }
            return "";
        }

        public static XmlDocument DownloadApiXml(string uri)
        {
            string text = "";
            XmlDocument apiXml = new XmlDocument();

            try
            {
                text = TryWebPageDownload(uri);
                if (text.Contains("URI Too Large"))
                {
                    var newException = new WikiApiException("Too large URI");

                    throw newException;
                }
                apiXml.LoadXml(text);
            }
            catch (Exception e)
            {
                e.Data.Add("Uri", uri);
                if (!string.IsNullOrEmpty(text))
                    e.Data.Add("Text", text);

                throw;
            }

            var errorNode = apiXml.SelectSingleNode("api/error");
            if (errorNode != null)
            {
                var newException = new WikiApiException("Api result error");
                newException.Data.Add("Uri", uri);
                newException.Data.Add("Text", text);

                var attrs = errorNode.Attributes;
                if (attrs != null)
                {
                    if (attrs["code"] != null)
                        newException.Data.Add("Code", attrs["code"].Value);
                    if (attrs["info"] != null)
                        newException.Data.Add("Info", attrs["info"].Value);
                }
                throw newException;
            }

            return apiXml;
        }

        private static string GetNewOffset(XmlDocument apiXml, string path, string name)
        {
            string newoffset = "";
            XmlNode continueNode = apiXml.DocumentElement.SelectSingleNode(path);
            if (continueNode != null &&
                continueNode.Attributes != null &&
                continueNode.Attributes[name] != null)
                newoffset = continueNode.Attributes[name].Value.Trim();
            return newoffset;
        }

        #endregion

        #region Project metadata

        public static Dictionary<string, Dictionary<string, string>> GetWikiCodes(string projectUrl)
        {
            var allCodes = new Dictionary<string, Dictionary<string, string>>();

            string uri =
                string.Format("https://{0}/w/api.php?format=xml&action=sitematrix", projectUrl);

            XmlDocument apiXml = DownloadApiXml(uri);


            XmlNodeList xmlSpecials = apiXml.DocumentElement.SelectNodes("//specials/special");
            Dictionary<string, string> specials = new Dictionary<string, string>();
            if (xmlSpecials != null)
                foreach (XmlNode xmlSpecial in xmlSpecials)
                {
                    if (xmlSpecial.Attributes == null ||
                        xmlSpecial.Attributes["dbname"] == null ||
                        xmlSpecial.Attributes["code"] == null)
                        continue;
                    var dbname = xmlSpecial.Attributes["dbname"].Value.Trim();
                    var code = xmlSpecial.Attributes["code"].Value.Trim();
                    if (!specials.ContainsKey(dbname))
                        specials.Add(dbname, code);
                }
            allCodes.Add("special", specials);
            XmlNodeList xmlLanguages = apiXml.DocumentElement.SelectNodes("//language");

            if (xmlLanguages != null)
                foreach (XmlNode xmlLanguage in xmlLanguages)
                {
                    if (xmlLanguage.Attributes == null)
                        continue;
                    var codeAttr = xmlLanguage.Attributes["code"];
                    if (codeAttr == null)
                        continue;
                    var langcode = codeAttr.Value.Trim();
                    XmlNodeList xmlSites = xmlLanguage.SelectNodes("./site/site");
                    if (xmlSites != null)
                        foreach (XmlNode xmlSite in xmlSites)
                        {
                            if (xmlSite.Attributes == null ||
                                xmlSite.Attributes["dbname"] == null ||
                                xmlSite.Attributes["code"] == null)
                                continue;
                            var dbname = xmlSite.Attributes["dbname"].Value.Trim();
                            var code = xmlSite.Attributes["code"].Value.Trim();
                            if (code == "wiki")
                                code = "wikipedia";
                            if (!allCodes.ContainsKey(code))
                            {
                                allCodes.Add(code, new Dictionary<string, string>());
                            }
                            allCodes[code].Add(dbname, langcode);
                        }
                }

            return allCodes;
        }

        public static Dictionary<int, string> GetNamespaces(string projectUrl)
        {
            var allNamespaces = new Dictionary<int, string>();

            if (string.IsNullOrEmpty(projectUrl))
                return allNamespaces;
            string uri =
                string.Format("https://{0}/w/api.php?format=xml&action=query&export&exportnowrap", projectUrl);

            XmlDocument apiXml = DownloadApiXml(uri);

            XmlNodeList xmlNamespaces = apiXml.DocumentElement.SelectNodes("//*[local-name()='namespace']");
            if (xmlNamespaces == null)
                return allNamespaces;
            foreach (XmlNode xmlNsNode in xmlNamespaces)
            {
                if (xmlNsNode.Attributes == null || xmlNsNode.Attributes["key"] == null)
                    continue;
                var strId = xmlNsNode.Attributes["key"].Value.Trim();
                var name = xmlNsNode.InnerText;
                int id;
                if (int.TryParse(strId, out id))
                {
                    allNamespaces.Add(id, name);
                }
            }

            return allNamespaces;
        }

        public static Dictionary<int, string> GetNamespaces(string code, string project)
        {
            var projectUrl = string.Format("{0}.{1}.org", code, project);

            return GetNamespaces(projectUrl);
        }
        
        #endregion  

        #region Page data functions
        /// <summary>
        /// Getting interwiki links from Wikidata
        /// </summary>
        /// <param name="wikicode">Wikidata code of project</param>
        /// <param name="titles">List of titles. Up to 50 items</param>
        /// <param name="returnWdTitle">Whether it is necessary to return Wikidata item id</param>
        /// <returns>Interwiki groups corresponded to input titles</returns>
        public static List<Dictionary<string, string>> GetWikidataLinks(string wikicode, string[] titles, bool returnWdTitle)
        {
            var allLinks = new List<Dictionary<string, string>>();
            if (titles.Length == 0 || string.IsNullOrEmpty(wikicode))
                return allLinks;
            var strTitles = HttpUtility.UrlEncode(string.Join("|", titles));
            if (wikicode == "wikidata")
            {
                strTitles = "ids=" + strTitles;
            }
            else
            {
                strTitles = string.Format("sites={0}&titles={1}", wikicode, strTitles);
            }
            string uri =
                string.Format("https://www.wikidata.org/w/api.php?format=xml&action=wbgetentities&props=sitelinks&{0}",
                              strTitles);
            XmlDocument apiXml = DownloadApiXml(uri);


            XmlNodeList xmlEntities = apiXml.DocumentElement.SelectNodes("//entity");
            if (xmlEntities == null)
                return allLinks;
            foreach (XmlNode xmlEntity in xmlEntities)
            {
                if (xmlEntity.Attributes == null)
                    continue;
                var pageLinks = new Dictionary<string, string>();
                var missingAttr = xmlEntity.Attributes["missing"];
                if (returnWdTitle)
                {
                    var idAttr = xmlEntity.Attributes["id"];
                    if (idAttr != null)
                    {
                        string wdTitle = idAttr.Value.Trim();
                        pageLinks.Add("wikidata", wdTitle);
                    }
                }
                XmlNodeList xmlLinks = xmlEntity.SelectNodes(".//sitelink");
                if (xmlLinks != null)
                    foreach (XmlNode xmlLink in xmlLinks)
                    {
                        if (xmlLink.Attributes == null)
                            continue;

                        string sitecode = xmlLink.Attributes["site"].Value.Trim();
                        string iwtitle = xmlLink.Attributes["title"].Value.Trim();

                        pageLinks.Add(sitecode, iwtitle);
                    }
                if (missingAttr == null)
                    allLinks.Add(pageLinks);
            }


            return allLinks;
        }

        public static Dictionary<string, Dictionary<string, string>> GetLocalInterwiki(string code, string project, string[] titles)
        {
            var projectUrl = string.Format("{0}.{1}.org", code, project);

            return GetLocalInterwiki(projectUrl, titles);
        }

        /// <summary>
        /// Getting local interwiki links
        /// </summary>
        /// <param name="projectUrl">Url of wiki project</param>
        /// <param name="titles">List of titles. Up to 50 items</param>
        /// <returns>Returns a dictionary containing key=input page title and value=local interwiki links</returns>
        public static Dictionary<string, Dictionary<string, string>> GetLocalInterwiki(string projectUrl, string[] titles)
        {
            var allLinks = new Dictionary<string, Dictionary<string, string>>();
            if (titles.Length == 0 || string.IsNullOrEmpty(projectUrl))
                return allLinks;
            var strTitles = HttpUtility.UrlEncode(string.Join("|", titles));
            string uri =
                    string.Format("https://{0}/w/api.php?action=query&format=xml&prop=langlinks&lllimit=500&titles={1}",
                                  projectUrl,
                                  strTitles);
            XmlDocument apiXml = DownloadApiXml(uri);


            XmlNodeList xmlItems = apiXml.DocumentElement.SelectNodes("//page");
            if (xmlItems == null)
                return allLinks;
            foreach (XmlNode xmlItem in xmlItems)
            {
                if (xmlItem.Attributes == null)
                    continue;
                var pageLangLinks = new Dictionary<string, string>();

                var title = xmlItem.Attributes["title"].Value;
                XmlNodeList xmlLangLinks = xmlItem.SelectNodes(".//ll");
                if (xmlLangLinks != null)
                    foreach (XmlNode xmlLangLink in xmlLangLinks)
                    {
                        if (xmlLangLink.Attributes == null)
                            continue;

                        var langcode = xmlLangLink.Attributes["lang"].Value;
                        var langlink = xmlLangLink.InnerText.Trim();
                        langlink = NormalizeTitle(langlink, langcode);
                        if (langcode.Length == 0 || langlink.Length == 0)
                            continue;
                        pageLangLinks.Add(langcode, langlink);
                    }
                allLinks.Add(title, pageLangLinks);
            }

            return allLinks;
        }

        /// <summary>
        /// This checks existance of local pages and their redirection state
        /// </summary>
        /// <param name="project">Project type</param>
        /// <param name="titles">List of titles. Up to 50 items</param>
        /// <param name="code">Language code</param>
        /// <returns>List of PageInfo objects</returns>
        public static List<PageInfo> GetProperTitles(string code, string project, string[] titles)
        {
            var projectUrl = string.Format("{0}.{1}.org", code, project);

            return GetProperTitles(projectUrl, titles);
        }

        /// <summary>
        /// This checks existance of local pages and their redirection state
        /// </summary>
        /// <param name="projectUrl">Url of wiki project</param>
        /// <param name="titles">List of titles. Up to 50 items</param>
        /// <returns>List of PageInfo objects</returns>
        public static List<PageInfo> GetProperTitles(string projectUrl, string[] titles)
        {
            var pages = new List<PageInfo>();
            if (titles.Length == 0 || string.IsNullOrEmpty(projectUrl))
                return pages;

            var strTitles = HttpUtility.UrlEncode(string.Join("|", titles));
            string uri =
                    string.Format("https://{0}/w/api.php?format=xml&action=query&redirects=&titles={1}",
                                  projectUrl,
                                  strTitles);
            XmlDocument apiXml = DownloadApiXml(uri);

            XmlNodeList xmlNormalizations = apiXml.DocumentElement.SelectNodes("//n");
            if (xmlNormalizations != null)
                foreach (XmlNode xmlNormalization in xmlNormalizations)
                {
                    if (xmlNormalization.Attributes == null)
                        continue;
                    var fromTitle = xmlNormalization.Attributes["from"].Value;
                    var toTitle = xmlNormalization.Attributes["to"].Value;
                    var newPage = new PageInfo
                        {
                            Title = fromTitle,
                            NormalizedTitle = toTitle
                        };
                    pages.Add(newPage);
                }
            XmlNodeList xmlRedirects = apiXml.DocumentElement.SelectNodes("//r");
            if (xmlRedirects != null)
                foreach (XmlNode xmlRedirect in xmlRedirects)
                {
                    if (xmlRedirect.Attributes == null)
                        continue;
                    var fromTitle = xmlRedirect.Attributes["from"].Value;
                    var toTitle = xmlRedirect.Attributes["to"].Value;
                    var sectionAttr = xmlRedirect.Attributes["tofragment"];
                    var newPage = new PageInfo
                        {
                            Title = fromTitle,
                            RedirectTo = toTitle
                        };
                    if (sectionAttr != null)
                        newPage.RedirectionSection = sectionAttr.Value.Trim();
                    pages.Add(newPage);
                }
            XmlNodeList xmlItems = apiXml.DocumentElement.SelectNodes("//page");
            if (xmlItems != null)
                foreach (XmlNode xmlItem in xmlItems)
                {
                    if (xmlItem.Attributes == null)
                        continue;
                    var title = xmlItem.Attributes["title"].Value;
                    var missingAttr = xmlItem.Attributes["missing"];
                    var nsAttr = xmlItem.Attributes["ns"];
                    int ns = -1;
                    if (nsAttr != null)
                    {
                        var res = int.TryParse(nsAttr.Value, out ns);
                        if (!res)
                            ns = -1;
                    }
                    var newPage = new PageInfo
                        {
                            Title = title,
                            IsMissing = missingAttr != null,
                            Namespace = ns
                        };
                    pages.Add(newPage);
                }

            for (int i = 0; i < pages.Count; i++)
            {
                if (pages[i].NormalizedTitle.Length == 0
                    && pages[i].RedirectTo.Length == 0)
                    continue;
                for (int j = i + 1; j < pages.Count; j++)
                {
                    if (pages[i].RedirectTo.Length > 0)
                    {
                        if (pages[i].RedirectTo != pages[j].Title)
                            continue;
                        pages[i].IsMissing = pages[j].IsMissing;
                        pages[i].Namespace = pages[j].Namespace;
                        break;
                    }
                    if (pages[i].NormalizedTitle != pages[j].Title)
                        continue;
                    if (pages[j].RedirectTo.Length > 0)
                    {
                        pages[i].RedirectTo = pages[j].RedirectTo;
                        pages[i].RedirectionSection = pages[j].RedirectionSection;
                    }
                    pages[i].IsMissing = pages[j].IsMissing;
                    pages[i].Namespace = pages[j].Namespace;
                }
            }

            return pages;
        }

        /// <summary>
        /// This checks existance of local page and its redirection state
        /// </summary>
        /// <param name="project">Project type</param>
        /// <param name="title">Local page title</param>
        /// <param name="code">Language code</param>
        /// <returns>PageInfo object</returns>
        public static PageInfo GetProperTitle(string code, string project, string title)
        {
            var projectUrl = string.Format("{0}.{1}.org", code, project);

            return GetProperTitle(projectUrl, title);
        }

        /// <summary>
        /// This checks existance of local page and its redirection state
        /// </summary>
        /// <param name="projectUrl">Url of wiki project</param>
        /// <param name="title">Local page title</param>
        /// <returns>PageInfo object</returns>
        public static PageInfo GetProperTitle(string projectUrl, string title)
        {
            var page = new PageInfo();
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(projectUrl))
                return page;

            page.Title = title;
            var strTitles = HttpUtility.UrlEncode(title);
            string uri =
                    string.Format("https://{0}/w/api.php?format=xml&action=query&redirects=&titles={1}",
                                  projectUrl,
                                  strTitles);
            XmlDocument apiXml = DownloadApiXml(uri);

            XmlNode xmlNormalization = apiXml.DocumentElement.SelectSingleNode("//n");
            if (xmlNormalization != null && xmlNormalization.Attributes != null)
            {
                page.NormalizedTitle = xmlNormalization.Attributes["to"].Value;
            }
            XmlNode xmlRedirect = apiXml.DocumentElement.SelectSingleNode("//r");
            if (xmlRedirect != null && xmlRedirect.Attributes != null)
            {
                page.RedirectTo = xmlRedirect.Attributes["to"].Value;
                var sectionAttr = xmlRedirect.Attributes["tofragment"];
                if (sectionAttr != null)
                    page.RedirectionSection = sectionAttr.Value.Trim();
            }
            XmlNode xmlItem = apiXml.DocumentElement.SelectSingleNode("//page");
            if (xmlItem != null && xmlItem.Attributes != null)
            {
                var properTitle = xmlItem.Attributes["title"].Value.Trim();
                var missingNode = xmlItem.Attributes["missing"];
                var nsAttr = xmlItem.Attributes["ns"];
                int ns = -1;
                if (nsAttr != null)
                {
                    var res = int.TryParse(nsAttr.Value, out ns);
                    if (!res)
                        ns = -1;
                }
                page.Namespace = ns;
                page.IsMissing = missingNode != null;
                if (page.Title != properTitle)
                    page.NormalizedTitle = properTitle;
            }

            return page;
        }

        private static string NormalizeTitle(string title, string code)
        {
            title = title.Replace("_", " ");

            return title.Trim();
        }

        private static DateTime GetTimeFromOffset(string offset)
        {
            DateTime newtime;

            bool res = DateTime.TryParse(offset, out newtime);
            if (!res)
                return new DateTime();

            newtime = newtime.ToUniversalTime();
            Console.WriteLine("Found new offset: {0:yyyy.MM.dd HH:mm:ss}", newtime);
            return newtime;
        }

        /// <summary>This gets revision list of some page</summary>
        /// <param name="projectUrl">Url of project</param>
        /// <param name="title">Page title</param>
        /// <returns>List of revisions</returns>
        public static List<PageInfo> GetRevisions(string projectUrl, string title)
        {
            var revisions = new List<PageInfo>();
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(projectUrl))
                return revisions;

            string uri =
                string.Format("https://{0}/w/api.php?format=xml&action=query&prop=revisions&rvprop=timestamp|ids|user|comment&rvlimit=500&titles={1}",
                              projectUrl,
                              title);
            Console.WriteLine("Try to get revisions for {0} page(s)", title.Count());
            XmlDocument apiXml = DownloadApiXml(uri);

            XmlNodeList xmlItems = apiXml.DocumentElement.SelectNodes("//rev");
            if (xmlItems == null)
                return revisions;
            foreach (XmlNode xmlItem in xmlItems)
            {
                if (xmlItem.Attributes != null)
                {
                    var pi = new PageInfo();
                    if (xmlItem.Attributes["user"] != null)
                        pi.User = xmlItem.Attributes["user"].Value;
                    if (xmlItem.Attributes["comment"] != null)
                        pi.Comment = xmlItem.Attributes["comment"].Value;
                    if (xmlItem.Attributes["revid"] != null)
                        pi.Title = xmlItem.Attributes["revid"].Value;
                    if (xmlItem.Attributes["parentid"] != null)
                        pi.RedirectTo = xmlItem.Attributes["parentid"].Value;
                    DateTime timestamp;
                    if (DateTime.TryParse(xmlItem.Attributes["timestamp"].Value, out timestamp))
                        pi.Timestamp = timestamp;
                    revisions.Add(pi);
                }
            }

            return revisions;
        }

        /// <summary>This gets revision list of some page</summary>
        /// <param name="code"></param>
        /// <param name="project"></param>
        /// <param name="title">Page title</param>
        /// <returns>List of revisions</returns>
        public static List<PageInfo> GetRevisions(string code, string project, string title)
        {
            var projectUrl = string.Format("{0}.{1}.org", code, project);

            return GetRevisions(projectUrl, title);
        }

        /// <summary>
        /// This gets difference between revisions
        /// </summary>
        /// <param name="projectUrl">Url of project</param>
        /// <param name="firstRev">First revion id</param>
        /// <param name="secondRev">Second revision id</param>
        /// <returns>Differece text</returns>
        public static string GetRevisionDiff(string projectUrl, string firstRev, string secondRev)
        {
            if (string.IsNullOrEmpty(firstRev) || string.IsNullOrEmpty(secondRev) || string.IsNullOrEmpty(projectUrl))
                return "";

            string uri =
                string.Format(
                    "https://{0}/w/api.php?format=xml&action=compare&fromrev={1}&torev={2}",
                    projectUrl,
                    firstRev,
                    secondRev);
            Console.WriteLine("Try to get revision difference");
            XmlDocument apiXml = DownloadApiXml(uri);

            XmlNode diffNode = apiXml.SelectSingleNode("//compare");
            if (diffNode != null)
                return diffNode.InnerText.Trim();

            return "";
        }

        /// <summary>
        /// This gets difference between revisions
        /// </summary>
        /// <param name="code"></param>
        /// <param name="project"></param>
        /// <param name="firstRev">First revion id</param>
        /// <param name="secondRev">Second revision id</param>
        /// <returns>Differece text</returns>
        public static string GetRevisionDiff(string code, string project, string firstRev, string secondRev)
        {
            var projectUrl = string.Format("{0}.{1}.org", code, project);

            return GetRevisionDiff(projectUrl, firstRev, secondRev);
        }

        /// <summary>
        /// This gets templates of one or group of pages
        /// </summary>
        /// <param name="projectUrl">Project url</param>
        /// <param name="titles">Page titles</param>
        /// <param name="limit">Limit</param>
        /// <returns>Dictionary with pages as keys and template lists as values.</returns>
        public static Dictionary<string, List<string>> GetTemplates(string projectUrl, string[] titles, int limit)
        {
            var allTemplates = new Dictionary<string, List<string>>();
            if (titles.Length == 0)
                return allTemplates;

            var strTitles = HttpUtility.UrlEncode(string.Join("|", titles));
            string uri =
                    string.Format("https://{0}/w/api.php?format=xml&action=query&prop=templates&tllimit=500&titles={1}",
                                  projectUrl,
                                  strTitles);

            Console.WriteLine("Try to get templates of {0} pages", titles.Length);
            XmlDocument apiXml = DownloadApiXml(uri);

            XmlNodeList xmlItems = apiXml.DocumentElement.SelectNodes("//page");
            if (xmlItems == null)
                return allTemplates;
            foreach (XmlNode xmlItem in xmlItems)
            {
                var pageTemplates = new List<string>();

                if (xmlItem.Attributes == null || xmlItem.Attributes["title"] == null)
                    continue;
                var title = xmlItem.Attributes["title"].Value;
                XmlNodeList xmlLangLinks = xmlItem.SelectNodes(".//tl");
                if (xmlLangLinks != null)
                    foreach (XmlNode xmlLangLink in xmlLangLinks)
                    {
                        if (xmlLangLink.Attributes == null)
                            continue;
                        var template = xmlLangLink.Attributes["title"].Value;
                        pageTemplates.Add(template);
                    }
                allTemplates.Add(title, pageTemplates);
            }

            return allTemplates;
        }

        /// <summary>
        /// This gets templates of one or group of pages
        /// </summary>
        /// <param name="code">Language codes</param>
        /// <param name="project">Project name</param>
        /// <param name="titles">Page titles</param>
        /// <param name="limit">Limit</param>
        /// <returns>Dictionary with pages as keys and template lists as values.</returns>
        public static Dictionary<string, List<string>> GetTemplates(string code, string project, string[] titles, int limit)
        {
            var projectUrl = string.Format("{0}.{1}.org", code, project);

            return GetTemplates(projectUrl, titles, limit);
        }

        /// <summary>
        /// This gets all internal links of one page or page group
        /// </summary>
        /// <param name="projectUrl">Project url</param>
        /// <param name="titles">Page titles</param>
        /// <param name="limit">Query limit</param>
        /// <param name="ns">Namespace</param>
        /// <returns>Dictionary of pages as keys and their links as values</returns>
        public static Dictionary<string, List<string>> GetPageLinks(string projectUrl, string[] titles, int limit, int ns)
        {
            var allLinks = new Dictionary<string, List<string>>();
            if (titles.Length == 0)
                return allLinks;

            var strTitles = HttpUtility.UrlEncode(string.Join("|", titles));
            string uri =
                    string.Format("https://{0}/w/api.php?format=xml&action=query&prop=links&pllimit=500&titles={1}&plnamespace={2}",
                                  projectUrl,
                                  strTitles,
                                  ns);

            Console.WriteLine("Try to get templates of {0} pages", titles.Length);
            XmlDocument apiXml = DownloadApiXml(uri);

            XmlNodeList xmlItems = apiXml.DocumentElement.SelectNodes("//page");
            if (xmlItems == null)
                return allLinks;
            foreach (XmlNode xmlItem in xmlItems)
            {
                var pageTemplates = new List<string>();

                if (xmlItem.Attributes == null || xmlItem.Attributes["title"] == null)
                    continue;
                var title = xmlItem.Attributes["title"].Value;
                XmlNodeList xmlLangLinks = xmlItem.SelectNodes(".//pl");
                if (xmlLangLinks != null)
                    foreach (XmlNode xmlLangLink in xmlLangLinks)
                    {
                        if (xmlLangLink.Attributes == null || xmlLangLink.Attributes["title"] == null)
                            continue;

                        var template = xmlLangLink.Attributes["title"].Value;
                        pageTemplates.Add(template);
                    }
                allLinks.Add(title, pageTemplates);
            }

            return allLinks;
        }

        /// <summary>
        /// This gets all internal links of one page or page group
        /// </summary>
        /// <param name="code">Language codes</param>
        /// <param name="project">Project name</param>
        /// <param name="titles">Page titles</param>
        /// <param name="limit">Query limit</param>
        /// <param name="ns">Namespace</param>
        /// <returns>Dictionary of pages as keys and their links as values</returns>
        public static Dictionary<string, List<string>> GetPageLinks(string code, string project, string[] titles, int limit, int ns)
        {
            var projectUrl = string.Format("{0}.{1}.org", code, project);

            return GetPageLinks(projectUrl, titles, limit, ns);
        }

        #endregion 

        # region Page list filling functions

        /// <summary>
        /// This fills page list with results of some query to wikisite API.
        /// </summary>
        /// <param name="pages">List of pages.</param>
        /// <param name="code">Language code. For Wikipedia, Wikisource, etc.</param>
        /// <param name="project">Project code. Wikipedia, wikisource, etc.</param>
        /// <param name="queryParams">Object with query parameters.</param>
        /// <returns>New offset.</returns>
        public static string FillFromApiQuery(List<PageInfo> pages, string code, string project, Dictionary<string, object> queryParams)
        {
            var projectUrl = string.Format("{0}.{1}.org", code.ToLower(), project.ToLower());

            return FillFromApiQuery(pages, projectUrl, queryParams);
        }

        /// <summary>
        /// This fills page list with results of some query to wikisite API.
        /// </summary>
        /// <param name="pages">List of pages.</param>
        /// <param name="projectUrl">Site url. Such as "en.wikipedia.org".</param>
        /// <param name="queryParams">Object with query parameters.</param>
        /// <returns>New offset.</returns>
        public static string FillFromApiQuery(List<PageInfo> pages, string projectUrl, Dictionary<string, object> queryParams)
        {
            if (!queryParams.ContainsKey("querytype"))
                return "";
            if (queryParams.ContainsKey("hours") || queryParams.ContainsKey("days") ||
                queryParams.ContainsKey("starttime"))
            {
                int totalhours = 0;
                if (queryParams.ContainsKey("hours"))
                    totalhours += (int)queryParams["hours"];
                if (queryParams.ContainsKey("days"))
                    totalhours += (int)queryParams["days"] * 24;
                DateTime startTime;
                DateTime endTime = DateTime.UtcNow;
                if (queryParams.ContainsKey("endtime"))
                {
                    endTime = (DateTime)queryParams["endtime"];
                }
                if (queryParams.ContainsKey("starttime"))
                    startTime = (DateTime)queryParams["starttime"];
                else
                    startTime = endTime.AddHours(-totalhours);

                string offset = endTime.ToString("s");
                while (true)
                {
                    offset = FillFromApiQuery(pages, projectUrl, queryParams, offset);
                    var pos = offset.IndexOf("|");
                    if (pos > 0)
                        offset = offset.Remove(pos).Trim();
                    if (GetTimeFromOffset(offset) < startTime)
                        break;
                }

                if (!queryParams.ContainsKey("timefiltration") || (int)queryParams["timefiltration"] != 0)
                {
                    foreach (var p in pages.Where(p => p.Timestamp > endTime || p.Timestamp < startTime).ToList())
                    {
                        pages.Remove(p);
                    }
                    Console.WriteLine("Pages after time filtration: {0}", pages.Count);
                }
                return "";
            }
            else if (queryParams.ContainsKey("getall") && (int)queryParams["getall"] == 1)
            {
                var offset = "";
                if (queryParams.ContainsKey("offset"))
                    offset = (string)queryParams["offset"];
                while (true)
                {
                    var newoffset = FillFromApiQuery(pages, projectUrl, queryParams, offset);
                    if (newoffset.Length == 0 || newoffset == offset)
                        break;
                    offset = newoffset;
                }
                return "";
            }
            else
            {
                var offset = "";
                if (queryParams.ContainsKey("offset"))
                    offset = (string)queryParams["offset"];
                return FillFromApiQuery(pages, projectUrl, queryParams, offset);
            }
        }

        /// <summary>
        /// This fills page list with results of some query to wikisite API.
        /// </summary>
        /// <param name="pages">List of pages.</param>
        /// <param name="projectUrl">Site url. Such as "en.wikipedia.org".</param>
        /// <param name="queryParams">Object with query parameters.</param>
        /// <param name="offset">Input offset.</param>
        /// <returns>New offset.</returns>
        public static string FillFromApiQuery(List<PageInfo> pages, string projectUrl, Dictionary<string, object> queryParams, string offset)
        {
            if (!queryParams.ContainsKey("querytype"))
                return "";
            string queryType = (string)queryParams["querytype"];
            switch (queryType)
            {
                case "newpages":
                    if (!queryParams.ContainsKey("withbots"))
                        queryParams.Add("withbots", 1);
                    return FillFromRecentChanges(pages, projectUrl, offset, true, queryParams);
                case "recentchanges":
                    return FillFromRecentChanges(pages, projectUrl, offset, false, queryParams);
                case "usercontributions":
                    return FillFromUserContributionsApi(pages, projectUrl, offset, queryParams);
                case "allpages":
                    return FillFromAllPagesApi(pages, projectUrl, offset, queryParams);
                case "log":
                    return FillFromLogApi(pages, projectUrl, offset, queryParams);
                case "linkstopage":
                    return FillFromLinksToPage(pages, projectUrl, offset, queryParams);
                case "transclusions":
                    return FillFromTransclusionsOfPage(pages, projectUrl, offset, queryParams);
                case "category":
                    return FillFromCategoryApi(pages, projectUrl, offset, queryParams);
                case "categorytree":
                    return FillFromCategoryTreeApi(pages, projectUrl, queryParams);
                case "abusefilterlog":
                    return FillFromAbuseFilterLog(pages, projectUrl, offset, queryParams);
                case "taggedrecentchanges":
                    return FillFromTaggedRecentChanges(pages, projectUrl, offset, queryParams);
                case "users":
                    return GetAllUsers(pages, projectUrl, offset, queryParams);
            }
            return "";
        }

        /// <summary>
        /// This fills page list with recent changes on wikisite.
        /// </summary>
        /// <param name="pages">Page list.</param>
        /// <param name="projectUrl">Site url.</param>
        /// <param name="offset">Input offset.</param>
        /// <param name="newPages">Fill page list with new pages or with all recent changes.</param>
        /// <param name="queryParams">Object with query parameters.</param>
        /// <returns>New offset.</returns>
        public static string FillFromRecentChanges(List<PageInfo> pages, string projectUrl, string offset, bool newPages, Dictionary<string, object> queryParams)
        {
            string uri =
                string.Format(
                    "https://{0}/w/api.php?format=xml&action=query&list=recentchanges&rcstart={1}&rcprop=title|user|timestamp",
                    projectUrl,
                    offset);
            bool withComments = queryParams.ContainsKey("withcomments") && (int)queryParams["withcomments"] == 1;
            if (withComments)
                uri += "|comment";
            if (queryParams.ContainsKey("limit"))
                uri += "&rclimit=" + (int)queryParams["limit"];
            if (queryParams.ContainsKey("ns"))
                uri += "&rcnamespace=" + (int)queryParams["ns"];
            if (newPages)
                uri += "&rctype=new";
            if (queryParams.ContainsKey("withbots") && (int)queryParams["withbots"] == 0)
                uri += "&rcshow=!bot";
            else
                uri += "&rcshow=!redirect";

            XmlDocument apiXml = DownloadApiXml(uri);

            XmlNodeList xmlItems = apiXml.DocumentElement.SelectNodes("//rc");
            if (xmlItems != null)
                foreach (XmlNode xmlItem in xmlItems)
                {
                    if (xmlItem.Attributes == null || xmlItem.Attributes["title"] == null)
                        continue;
                    var ttitle = xmlItem.Attributes["title"].Value.Trim();
                    var tuser = "";
                    if (xmlItem.Attributes["user"] != null)
                        tuser = xmlItem.Attributes["user"].Value.Trim();
                    var tcomment = "";
                    if (withComments && xmlItem.Attributes["comment"] != null)
                        tcomment = xmlItem.Attributes["comment"].Value.Trim();
                    var p = new PageInfo
                        {
                            Title = ttitle,
                            User = tuser,
                            Comment = tcomment
                        };
                    if (xmlItem.Attributes["timestamp"] != null)
                    {
                        DateTime ttimestamp;
                        if (DateTime.TryParse(xmlItem.Attributes["timestamp"].Value.Trim(), out ttimestamp))
                            p.Timestamp = ttimestamp;
                    }
                    pages.Add(p);
                }
            string newoffset = GetNewOffset(apiXml, "//query-continue/recentchanges", "rccontinue");

            Console.WriteLine("Total found {0} pages", pages.Count);
            return newoffset;
        }

        /// <summary>
        /// This gets list of last edited by some user pages
        /// </summary>
        /// <param name="pages">Page list.</param>
        /// <param name="projectUrl">Site url.</param>
        /// <param name="offset">Input offset.</param>
        /// <param name="queryParams">Object with query parameters.</param>
        /// <returns>New offset.</returns>
        public static string FillFromUserContributionsApi(List<PageInfo> pages, string projectUrl, string offset, Dictionary<string, object> queryParams)
        {
            if (!queryParams.ContainsKey("username"))
                return "";
            string propuser = (string)queryParams["username"];

            string uri =
                string.Format(
                    "https://{0}/w/api.php?format=xml&action=query&list=usercontribs&ucstart={1}&ucuser={2}",
                    projectUrl,
                    offset,
                    propuser);
            if (queryParams.ContainsKey("limit"))
                uri += "&uclimit=" + (int)queryParams["limit"];
            if (queryParams.ContainsKey("ns"))
                uri += "&ucnamespace=" + (int)queryParams["ns"];

            //Console.WriteLine("Try to get {0} pages from user contributions", limit);
            XmlDocument apiXml = DownloadApiXml(uri);

            XmlNodeList xmlItems = apiXml.DocumentElement.SelectNodes("//item");
            if (xmlItems != null)
                foreach (XmlNode xmlItem in xmlItems)
                {
                    if (xmlItem.Attributes == null || xmlItem.Attributes["title"] == null)
                        continue;
                    var ttitle = xmlItem.Attributes["title"].Value.Trim();
                    var tuser = "";
                    if (xmlItem.Attributes["user"] != null)
                        tuser = xmlItem.Attributes["user"].Value.Trim();
                    var tcomment = "";
                    if (xmlItem.Attributes["comment"] != null)
                        tcomment = xmlItem.Attributes["comment"].Value.Trim();
                    var p = new PageInfo
                        {
                            Title = ttitle,
                            User = tuser,
                            Comment = tcomment
                        };
                    var res = DateTime.TryParse(xmlItem.Attributes["timestamp"].Value.Trim(), out p.Timestamp);
                    if (res)
                        p.Timestamp = p.Timestamp.ToUniversalTime();
                    pages.Add(p);
                }
            string newoffset = GetNewOffset(apiXml, "//query-continue/usercontribs", "ucstart");

            Console.WriteLine("Total found {0} pages", pages.Count);
            return newoffset;
        }

        /// <summary>
        /// This gets alphabetic range of pages
        /// </summary>
        /// <param name="pages">Page list.</param>
        /// <param name="projectUrl">Site url.</param>
        /// <param name="fromStr">Input offset.</param>
        /// <param name="queryParams">Object with query parameters.</param>
        /// <returns>New offset.</returns>
        public static string FillFromAllPagesApi(List<PageInfo> pages, string projectUrl, string fromStr, Dictionary<string, object> queryParams)
        {
            string uri =
                string.Format("https://{0}/w/api.php?format=xml&action=query&list=allpages&apfrom={1}",
                              projectUrl,
                              fromStr);
            if (queryParams.ContainsKey("limit"))
                uri += "&aplimit=" + (int)queryParams["limit"];
            if (queryParams.ContainsKey("ns"))
                uri += "&apnamespace=" + (int)queryParams["ns"];
            if (queryParams.ContainsKey("redirtype"))
                uri += "&apfilterredir=" + (string)queryParams["redirtype"];

            //      Console.WriteLine("Try to get {0} pages from all pages", limit);
            XmlDocument apiXml = DownloadApiXml(uri);

            XmlNodeList xmlItems = apiXml.DocumentElement.SelectNodes("//p");
            if (xmlItems != null)
                foreach (XmlNode xmlItem in xmlItems)
                {
                    if (xmlItem.Attributes == null || xmlItem.Attributes["title"] == null)
                        continue;
                    var ttitle = xmlItem.Attributes["title"].Value.Trim();
                    var p = new PageInfo
                        {
                            Title = ttitle
                        };
                    pages.Add(p);
                }
            string newoffset = GetNewOffset(apiXml, "//query-continue/allpages", "apcontinue");

            return newoffset;
        }

        /// <summary>This gets all pages from a specified category.</summary>
        /// <param name="pages">Page list.</param>
        /// <param name="projectUrl">Site url.</param>
        /// <param name="queryParams">Object with query parameters.</param>
        public static void FillFromCategoryApi(List<PageInfo> pages, string projectUrl, Dictionary<string, object> queryParams)
        {
            var offset = "";

            while (true)
            {
                offset = FillFromCategoryApi(pages, projectUrl, offset, queryParams);

                if (offset.Length == 0)
                    break;
            }
        }

        /// <summary>
        /// This gets some pages from a specified category.
        /// </summary>
        /// <param name="pages">Page list.</param>
        /// <param name="projectUrl">Site url.</param>
        /// <param name="offset">Input offset.</param>
        /// <param name="queryParams">Object with query parameters.</param>
        /// <returns>New offset.</returns>
        public static string FillFromCategoryApi(List<PageInfo> pages, string projectUrl, string offset, Dictionary<string, object> queryParams)
        {
            if (!queryParams.ContainsKey("categoryname") || ((string)queryParams["categoryname"]).Length == 0)
                return "";
            var categoryName = (string)queryParams["categoryname"];
            var pos = categoryName.IndexOf(":");
            if (pos > 0)
                categoryName = categoryName.Substring(pos + 1).Trim();
            categoryName = "Category:" + categoryName;
            Console.WriteLine("Getting category \"{0}\" contents...", categoryName);

            string uri =
                string.Format("https://{0}/w/api.php?format=xml&action=query&list=categorymembers&cmtitle={1}&cmsort=sortkey",
                          projectUrl,
                          categoryName);
            if (!string.IsNullOrEmpty(offset))
                uri += "&cmcontinue=" + offset;
            if (queryParams.ContainsKey("limit"))
                uri += "&cmlimit=" + (int)queryParams["limit"];
            if (queryParams.ContainsKey("ns"))
                uri += "&cmnamespace=" + (int)queryParams["ns"];
            XmlDocument apiXml = DownloadApiXml(uri);

            XmlNodeList xmlItems = apiXml.DocumentElement.SelectNodes("//cm");
            if (xmlItems != null)
                foreach (XmlNode xmlItem in xmlItems)
                {
                    if (xmlItem.Attributes == null || xmlItem.Attributes["title"] == null || xmlItem.Attributes["ns"] == null)
                        continue;
                    var respTitle = xmlItem.Attributes["title"].Value.Trim();
                    int respNs;
                    if (!int.TryParse(xmlItem.Attributes["ns"].Value.Trim(), out respNs))
                        respNs = -1;

                    pages.Add(new PageInfo { Title = respTitle, Namespace = respNs });
                }
            var newoffset = GetNewOffset(apiXml, "//query-continue/categorymembers", "cmcontinue");

            return newoffset;
        }

        /// <summary>
        /// This gets subcategories from a specified category.
        /// </summary>
        /// <param name="pages">Page list.</param>
        /// <param name="projectUrl">Site url.</param>
        /// <param name="queryParams">Object with query parameters.</param>
        public static void FillSubsFromCategoryApi(List<PageInfo> pages, string projectUrl, Dictionary<string, object> queryParams)
        {
            if (queryParams.ContainsKey("ns"))
                queryParams["ns"] = 14;
            else
                queryParams.Add("ns", 14);

            FillFromCategoryApi(pages, projectUrl, queryParams);
        }

        /// <summary>This gets all pages from a specified category tree.</summary>
        /// <param name="pages">Page list.</param>
        /// <param name="projectUrl">Site url.</param>
        /// <param name="queryParams">Object with query parameters.</param>
        /// <returns>Always empty.</returns>
        public static string FillFromCategoryTreeApi(List<PageInfo> pages, string projectUrl, Dictionary<string, object> queryParams)
        {
            if (!queryParams.ContainsKey("categoryname") || ((string)queryParams["categoryname"]).Length == 0)
                return "";
            var categoryName = (string)queryParams["categoryname"];
            var pos = categoryName.IndexOf(":");
            if (pos > 0)
                categoryName = categoryName.Substring(pos + 1).Trim();
            categoryName = "Category:" + categoryName;
            Console.WriteLine("Getting category \"{0}\" contents...", categoryName);

            var catTree = new List<PageInfo>
                {
                    new PageInfo {Title = categoryName}
                };
            int depth = -1;
            if (queryParams.ContainsKey("depth"))
                depth = (int)queryParams["depth"];
            int depthIndex = 0;
            int curLast = 0;
            while (depth < 0 || depthIndex < depth)
            {
                int prevLast = curLast;
                curLast = catTree.Count;
                if (prevLast == curLast)
                    break;
                for (int i = prevLast; i < curLast; i++)
                {
                    var newQueryParams = new Dictionary<string, object> { { "categoryname", catTree[i].Title } };
                    if (queryParams.ContainsKey("limit"))
                        newQueryParams.Add("limit", queryParams["limit"]);
                    FillSubsFromCategoryApi(catTree, projectUrl, queryParams);
                }
                for (int i = catTree.Count - 1; i >= curLast; i--)
                {
                    if (catTree.Count(cat => cat.Title == catTree[i].Title) >= 2)
                        catTree.RemoveAt(i);
                }

                depthIndex++;
            }

            Console.WriteLine("Total categories: {0}", catTree.Count);
            foreach (var cat in catTree)
            {
                var newQueryParams = new Dictionary<string, object> { { "categoryname", cat.Title } };
                if (queryParams.ContainsKey("limit"))
                    newQueryParams.Add("limit", queryParams["limit"]);
                FillFromCategoryApi(pages, projectUrl, queryParams);
            }
            Console.WriteLine("Total pages with doubles: {0}", pages.Count);
            pages = pages.GroupBy(p => p.Title).Select(g => g.First()).ToList();
            Console.WriteLine("total pages without doubls: {0}", pages.Count);
            return "";
        }

        /// <summary>
        /// This fills page list with some log entries.
        /// </summary>
        /// <param name="pages">Page list.</param>
        /// <param name="projectUrl">Site url.</param>
        /// <param name="offset">Input offset.</param>
        /// <param name="queryParams">Object with query parameters.</param>
        /// <returns>New offset.</returns>
        public static string FillFromLogApi(List<PageInfo> pages, string projectUrl, string offset, Dictionary<string, object> queryParams)
        {
            if (!queryParams.ContainsKey("type"))
                return "";
            string type = (string)queryParams["type"];
            string uri =
                string.Format(
                    "https://{0}/w/api.php?format=xml&action=query&list=recentchanges&lestart={1}&list=logevents&letype={2}&leprop=comment|user|timestamp|title",
                    projectUrl,
                    offset,
                    type);
            if (type == "move")
                uri += "|details";
            if (queryParams.ContainsKey("limit"))
                uri += "&lelimit=" + (int)queryParams["limit"];
            if (queryParams.ContainsKey("title"))
                uri += "&letitle=" + (string)queryParams["title"];

            XmlDocument apiXml = DownloadApiXml(uri);

            XmlNodeList xmlItems = apiXml.DocumentElement.SelectNodes("//item");
            if (xmlItems != null)
                foreach (XmlNode xmlItem in xmlItems)
                {
                    string addTitle = "";
                    if (xmlItem.Attributes == null || xmlItem.Attributes["title"] == null)
                        continue;
                    string mainTitle = xmlItem.Attributes["title"].Value.Trim();
                    if (type == "move")
                    {
                        XmlNode addNode = xmlItem.FirstChild;
                        if (addNode == null || addNode.Attributes == null)
                            continue;
                        addTitle = addNode.Attributes["new_title"].Value.Trim();
                    }

                    var tcomment = "";
                    if (xmlItem.Attributes["comment"] != null)
                        tcomment = xmlItem.Attributes["comment"].Value.Trim();
                    var pi = new PageInfo();
                    var tuser = "";
                    if (xmlItem.Attributes["user"] != null)
                        tuser = xmlItem.Attributes["user"].Value.Trim();
                    pi.Comment = tcomment;
                    pi.User = tuser;
                    DateTime.TryParse(xmlItem.Attributes["timestamp"].Value.Trim(), out pi.Timestamp);
                    pi.Timestamp = pi.Timestamp.ToUniversalTime();
                    pi.Title = mainTitle;
                    if (type == "move")
                    {
                        pi.RedirectTo = addTitle;
                    }
                    pages.Add(pi);
                }
            string newoffset = GetNewOffset(apiXml, "//query-continue/logevents", "lestart");

            Console.WriteLine("Total found {0} pages", pages.Count);
            return newoffset;
        }

        /// <summary>
        /// This fills page list with links to some page.
        /// </summary>
        /// <param name="pages">Page list.</param>
        /// <param name="projectUrl">Site url.</param>
        /// <param name="offset">Input offset.</param>
        /// <param name="queryParams">Object with query parameters.</param>
        /// <returns>New offset.</returns>
        public static string FillFromLinksToPage(List<PageInfo> pages, string projectUrl, string offset, Dictionary<string, object> queryParams)
        {
            if (!queryParams.ContainsKey("title") || ((string)queryParams["title"]).Length == 0)
            {
                Console.WriteLine("Incorrect title");
                return "";
            }
            string title = (string)queryParams["title"];

            string uri =
                string.Format(
                    "https://{0}/w/api.php?format=xml&action=query&list=backlinks&bltitle={1}",
                    projectUrl,
                    title);
            if (queryParams.ContainsKey("limit"))
                uri += "&bllimit=" + (int)queryParams["limit"];
            if (queryParams.ContainsKey("ns"))
                uri += "&blnamespace=" + (int)queryParams["ns"];
            if (offset.Length > 0)
                uri += "&blcontinue=" + offset;
            if (queryParams.ContainsKey("withredirects") && (int)queryParams["withredirects"] == 1)
                uri += "&blredirect";

            Console.WriteLine("Try to get links to page: {0}", title);
            XmlDocument apiXml = DownloadApiXml(uri);

            XmlNodeList xmlItems = apiXml.DocumentElement.SelectNodes("//bl");
            if (xmlItems != null)
                foreach (XmlNode xmlItem in xmlItems)
                {
                    if (xmlItem.Attributes == null || xmlItem.Attributes["title"] == null)
                        continue;
                    var nsStr = xmlItem.Attributes["ns"].Value.Trim();
                    int outNs;
                    if (int.TryParse(nsStr, out outNs))
                        outNs = -1;

                    pages.Add(new PageInfo { Title = xmlItem.Attributes["title"].Value, Namespace = outNs });
                }
            string newoffset = GetNewOffset(apiXml, "//query-continue/backlinks", "blcontinue");

            return newoffset;
        }

        /// <summary>
        /// This fills with transclusions of a specified page.
        /// </summary>
        /// <param name="pages">Page list.</param>
        /// <param name="projectUrl">Site url.</param>
        /// <param name="offset">Input offset.</param>
        /// <param name="queryParams">Object with query parameters.</param>
        /// <returns>New offset.</returns>
        public static string FillFromTransclusionsOfPage(List<PageInfo> pages, string projectUrl, string offset, Dictionary<string, object> queryParams)
        {
            if (!queryParams.ContainsKey("title") || ((string)queryParams["title"]).Length == 0)
            {
                Console.WriteLine("Incorrect title");
                return "";
            }
            string title = (string)queryParams["title"];

            string uri =
                string.Format(
                    "https://{0}/w/api.php?format=xml&action=query&list=embeddedin&eititle={1}",
                    projectUrl,
                    title);
            if (queryParams.ContainsKey("limit"))
                uri += "&eilimit=" + (int)queryParams["limit"];
            if (queryParams.ContainsKey("ns"))
                uri += "&einamespace=" + (int)queryParams["ns"];
            if (!string.IsNullOrEmpty(offset))
                uri += "&eicontinue=" + offset;

            Console.WriteLine("Try to get links to page: {0}", title);
            XmlDocument apiXml = DownloadApiXml(uri);

            XmlNodeList xmlItems = apiXml.DocumentElement.SelectNodes("//ei");
            if (xmlItems != null)
                foreach (XmlNode xmlItem in xmlItems)
                {
                    if (xmlItem.Attributes == null || xmlItem.Attributes["title"] == null)
                        continue;
                    var nsStr = xmlItem.Attributes["ns"].Value.Trim();
                    int outNs;
                    if (int.TryParse(nsStr, out outNs))
                        outNs = -1;

                    pages.Add(new PageInfo { Title = xmlItem.Attributes["title"].Value, Namespace = outNs });
                }
            string newoffset = GetNewOffset(apiXml, "//query-continue/embeddedin", "eicontinue");

            return newoffset;
        }

        /// <summary>
        /// This fills page list with a specifified abuse filter log entries.
        /// </summary>
        /// <param name="pages">Page list.</param>
        /// <param name="projectUrl">Site url.</param>
        /// <param name="offset">Input offset.</param>
        /// <param name="queryParams">Object with query parameters.</param>
        /// <returns>New offset.</returns>
        public static string FillFromAbuseFilterLog(List<PageInfo> pages, string projectUrl, string offset, Dictionary<string, object> queryParams)
        {
            if (!queryParams.ContainsKey("filterid") || ((string)queryParams["filterid"]).Length == 0)
            {
                Console.WriteLine("Incorrect title");
                return "";
            }
            string filterId = (string)queryParams["filterid"];

            string uri =
                string.Format(
                    "https://{0}/w/api.php?format=xml&action=query&list=abuselog&aflfilter={1}&aflstart={2}&aflprop=ids|user|title|timestamp|revid",
                    projectUrl,
                    filterId,
                    offset);
            if (queryParams.ContainsKey("limit"))
                uri += "&aflimit=" + (int)queryParams["limit"];

            XmlDocument apiXml = DownloadApiXml(uri);

            XmlNodeList xmlItems = apiXml.DocumentElement.SelectNodes("//item");
            if (xmlItems != null)
                foreach (XmlNode xmlItem in xmlItems)
                {
                    if (xmlItem.Attributes == null || xmlItem.Attributes["title"] == null)
                        continue;
                    string mainTitle = xmlItem.Attributes["title"].Value.Trim();

                    var tcomment = "";
                    if (xmlItem.Attributes["revid"] != null)
                        tcomment = xmlItem.Attributes["revid"].Value.Trim();
                    var pi = new PageInfo();
                    var tuser = "";
                    if (xmlItem.Attributes["user"] != null)
                        tuser = xmlItem.Attributes["user"].Value.Trim();
                    pi.Comment = tcomment;
                    pi.User = tuser;
                    DateTime.TryParse(xmlItem.Attributes["timestamp"].Value.Trim(), out pi.Timestamp);
                    pi.Timestamp = pi.Timestamp.ToUniversalTime();
                    pi.Title = mainTitle;

                    Console.WriteLine(pi.Title);
                    pages.Add(pi);
                }
            string newoffset = GetNewOffset(apiXml, "//query-continue/abuselog", "aflstart");

            Console.WriteLine("Total found {0} pages", pages.Count);
            return newoffset;
        }

        /// <summary>
        /// This fills page list with tagged recent changes.
        /// </summary>
        /// <param name="pages">Page list.</param>
        /// <param name="projectUrl">Site url.</param>
        /// <param name="offset">Input offset.</param>
        /// <param name="queryParams">Object with query parameters.</param>
        /// <returns>New offset.</returns>
        public static string FillFromTaggedRecentChanges(List<PageInfo> pages, string projectUrl, string offset, Dictionary<string, object> queryParams)
        {
            if (!queryParams.ContainsKey("tag") || ((string)queryParams["tag"]).Length == 0)
            {
                Console.WriteLine("Incorrect title");
                return "";
            }
            string tag = (string)queryParams["tag"];

            string uri =
                string.Format(
                    "https://{0}/w/api.php?format=xml&action=query&list=recentchanges&rcprop=ids|timestamp|title|user&rctag={1}&rcstart={2}",
                    projectUrl,
                    tag,
                    offset);
            if (queryParams.ContainsKey("limit"))
                uri += "&rclimit=" + (int)queryParams["limit"];
            if (queryParams.ContainsKey("patrollstatus"))
            {
                int patrollStatus = (int)queryParams["patrollstatus"];
                if (patrollStatus == -1)
                    uri += "&rcshow=!patrolled";
                if (patrollStatus == 1)
                    uri += "&rcshow=patrolled";
            }

            XmlDocument apiXml = DownloadApiXml(uri);

            XmlNodeList xmlItems = apiXml.DocumentElement.SelectNodes("//rc");
            if (xmlItems != null)
                foreach (XmlNode xmlItem in xmlItems)
                {
                    if (xmlItem.Attributes == null || xmlItem.Attributes["title"] == null)
                        continue;
                    string mainTitle = xmlItem.Attributes["title"].Value.Trim();

                    var rev = "";
                    if (xmlItem.Attributes["revid"] != null)
                        rev = xmlItem.Attributes["revid"].Value.Trim();
                    var oldrev = "";
                    if (xmlItem.Attributes["old_revid"] != null)
                        oldrev = xmlItem.Attributes["old_revid"].Value.Trim();
                    var pi = new PageInfo();
                    var tuser = "";
                    if (xmlItem.Attributes["user"] != null)
                        tuser = xmlItem.Attributes["user"].Value.Trim();
                    pi.Comment = rev;
                    pi.RedirectTo = oldrev;
                    pi.User = tuser;
                    DateTime.TryParse(xmlItem.Attributes["timestamp"].Value.Trim(), out pi.Timestamp);
                    pi.Timestamp = pi.Timestamp.ToUniversalTime();
                    pi.Title = mainTitle;

                    Console.WriteLine(pi.Title);
                    pages.Add(pi);
                }
            string newoffset = GetNewOffset(apiXml, "//query-continue/recentchanges", "rccontinue");

            Console.WriteLine("Total found {0} pages", pages.Count);
            return newoffset;
        }

        /// <summary>
        /// This get all project users or users of a specified group.
        /// </summary>
        /// <param name="pages">Page list.</param>
        /// <param name="projectUrl">Site url.</param>
        /// <param name="offset">Input offset.</param>
        /// <param name="queryParams">Object with query parameters.</param>
        /// <returns>New offset.</returns>
        public static string GetAllUsers(List<PageInfo> pages, string projectUrl, string offset, Dictionary<string, object> queryParams)
        {
            string uri =
                string.Format(
                    "https://{0}/w/api.php?format=xml&action=query&list=allusers",
                    projectUrl);
            if (queryParams.ContainsKey("limit"))
                uri += "&aulimit=" + (int)queryParams["limit"];
            if (queryParams.ContainsKey("group"))
                uri += "&augroup=" + (string)queryParams["group"];
            if (offset.Length > 0)
                uri += "&aufrom=" + offset;

            //      Console.WriteLine("Try to get users of group: {0}", group);
            XmlDocument apiXml = DownloadApiXml(uri);

            XmlNodeList xmlItems = apiXml.DocumentElement.SelectNodes("//u");
            if (xmlItems != null)
                foreach (XmlNode xmlItem in xmlItems)
                {
                    if (xmlItem.Attributes == null || xmlItem.Attributes["name"] == null)
                        continue;

                    pages.Add(new PageInfo { Title = xmlItem.Attributes["name"].Value });
                }

            string newoffset = GetNewOffset(apiXml, "//query-continue/allusers", "aufrom");

            Console.WriteLine("Total found {0} users", pages.Count);
            return newoffset;
        }

        #endregion
    }
}