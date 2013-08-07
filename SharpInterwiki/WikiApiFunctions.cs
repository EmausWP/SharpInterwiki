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
        public DateTime Timestamp;
        public string User;
        public string Comment;
        public int Namespace = -1;
        public string NormalizedTitle = "";
        public string RedirectTo = "";
        public string RedirectionSection = "";
        public bool IsMissing = false;
    }

    public class WikiApiFunctions
    {
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

        public static Dictionary<string, Dictionary<string, string>> GetWikiCodes()
        {
            var allCodes = new Dictionary<string, Dictionary<string, string>>();

            string uri =
                string.Format("http://meta.wikimedia.org/w/api.php?action=sitematrix&format=xml");
            try
            {
                string xmlText = WebPageDownload(uri);
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlText);
                XmlNodeList xmlSpecials = doc.DocumentElement.SelectNodes("//specials/special");
                Dictionary<string, string> specials = new Dictionary<string, string>();
                foreach (XmlNode xmlSpecial in xmlSpecials)
                {
                    var dbname = xmlSpecial.Attributes["dbname"].Value.Trim();
                    var code = xmlSpecial.Attributes["code"].Value.Trim();
                    if (!specials.ContainsKey(dbname))
                        specials.Add(dbname, code);
                }
                allCodes.Add("special", specials);
                XmlNodeList xmlLanguages = doc.DocumentElement.SelectNodes("//language");
                foreach (XmlNode xmlLanguage in xmlLanguages)
                {
                    var codeAttr = xmlLanguage.Attributes["code"];
                    if (codeAttr == null) 
                        continue;
                    var langcode = codeAttr.Value.Trim();
                    XmlNodeList xmlSites = xmlLanguage.SelectNodes("./site/site");
                    foreach (XmlNode xmlSite in xmlSites)
                    {
                        var dbname = xmlSite.Attributes["dbname"].Value.Trim();
                        var code = xmlSite.Attributes["code"].Value.Trim();
                        if (!allCodes.ContainsKey(code))
                        {
                            allCodes.Add(code, new Dictionary<string, string>());
                        }
                        allCodes[code].Add(dbname, langcode);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return allCodes;
        }

        public static List<Dictionary<string, string>> GetWikidataLinks(string code, string[] titles, bool returnWdTitle)
        {
            var allLinks = new List<Dictionary<string, string>>();
            if (titles.Length == 0 || string.IsNullOrEmpty(code))
                return allLinks;
            var strTitles = HttpUtility.UrlEncode(string.Join("|", titles));
            string uri =
                string.Format("http://www.wikidata.org/w/api.php?action=wbgetentities&format=xml&props=sitelinks&sites={0}wiki&titles={1}",
                              code.Replace("-", "_"),
                              strTitles);
            try
            {
                string xmlText = WebPageDownload(uri);
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlText);
                XmlNodeList xmlEntities = doc.DocumentElement.SelectNodes("//entity");
                foreach (XmlNode xmlEntity in xmlEntities)
                {
                    var pageLinks = new Dictionary<string, string>();
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
                    foreach (XmlNode xmlLink in xmlLinks)
                    {
                        if (xmlLink.Attributes == null)
                            continue;

                        string sitecode = xmlLink.Attributes["site"].Value.Trim();
                        string iwtitle = xmlLink.Attributes["title"].Value.Trim();

                        pageLinks.Add(sitecode, iwtitle);
                    }
                    allLinks.Add(pageLinks);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return allLinks;
        }

        public static Dictionary<string, Dictionary<string, string>> GetLocalInterwiki(string code, string[] titles)
        {
            var allLinks = new Dictionary<string, Dictionary<string, string>>();
            if (titles.Length == 0 || string.IsNullOrEmpty(code))
                return allLinks;
            var strTitles = HttpUtility.UrlEncode(string.Join("|", titles));
            string uri =
                    string.Format("http://{0}.wikipedia.org/w/api.php?action=query&format=xml&prop=langlinks&lllimit=500&titles={1}",
                                  code,
                                  strTitles);
            try
            {
                string xmlText = WebPageDownload(uri);
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlText);
                XmlNodeList xmlPages = doc.DocumentElement.SelectNodes("//page");
                foreach (XmlNode xmlPage in xmlPages)
                {
                    var pageLangLinks = new Dictionary<string, string>();

                    var title = xmlPage.Attributes["title"].Value;
                    XmlNodeList xmlLangLinks = xmlPage.SelectNodes(".//ll");
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
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return allLinks;
        }
        
        public static List<PageInfo> GetProperTitles(string code, string[] titles)
        {
            var pages = new List<PageInfo>();
            if (titles.Length == 0 || string.IsNullOrEmpty(code))
                return pages;

            var strTitles = HttpUtility.UrlEncode(string.Join("|", titles));
            try
            {
                string uri =
                    string.Format("http://{0}.wikipedia.org/w/api.php?action=query&format=xml&redirects=&titles={1}",
                                  code,
                                  strTitles);

                string xmlText = WebPageDownload(uri);
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlText);
                XmlNodeList xmlNormalizations = doc.DocumentElement.SelectNodes("//n");
                foreach (XmlNode xmlNormalization in xmlNormalizations)
                {
                    var fromTitle = xmlNormalization.Attributes["from"].Value;
                    var toTitle = xmlNormalization.Attributes["to"].Value;
                    var newPage = new PageInfo
                        {
                            Title = fromTitle,
                            NormalizedTitle = toTitle
                        };
                    pages.Add(newPage);
                }
                XmlNodeList xmlRedirects = doc.DocumentElement.SelectNodes("//r");
                foreach (XmlNode xmlRedirect in xmlRedirects)
                {
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
                XmlNodeList xmlPages = doc.DocumentElement.SelectNodes("//page");
                foreach (XmlNode xmlPage in xmlPages)
                {
                    var title = xmlPage.Attributes["title"].Value;
                    var missingAttr = xmlPage.Attributes["missing"];
                    var nsAttr = xmlPage.Attributes["ns"];
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
                    if(pages[i].NormalizedTitle.Length == 0 
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
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return pages;
        }

        public static PageInfo GetProperTitle(string code, string title)
        {
            var page = new PageInfo();
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(code))
                return page;

            page.Title = title;
            var strTitles = HttpUtility.UrlEncode(title);
            try
            {
                string uri =
                    string.Format("http://{0}.wikipedia.org/w/api.php?action=query&format=xml&redirects=&titles={1}",
                                  code,
                                  strTitles);

                string xmlText = WebPageDownload(uri);
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlText);
                XmlNode xmlNormalization = doc.DocumentElement.SelectSingleNode("//n");
                if(xmlNormalization != null)
                {
                    page.NormalizedTitle = xmlNormalization.Attributes["to"].Value;
                }
                XmlNode xmlRedirect = doc.DocumentElement.SelectSingleNode("//r");
                if(xmlRedirect != null)
                {
                    page.RedirectTo = xmlRedirect.Attributes["to"].Value;
                    var sectionAttr = xmlRedirect.Attributes["tofragment"];
                    if (sectionAttr != null)
                        page.RedirectionSection = sectionAttr.Value.Trim();
                }
                XmlNode xmlPage = doc.DocumentElement.SelectSingleNode("//page");
                if(xmlPage != null)
                {
                    var properTitle = xmlPage.Attributes["title"].Value.Trim();
                    var missingNode = xmlPage.Attributes["missing"];
                    var nsAttr = xmlPage.Attributes["ns"];
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
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return page;
        }

        public static string NormalizeTitle(string title, string code)
        {
            title = title.Replace("_", " ");

            return title.Trim();
        }

        public static string FillFromNewPagesApi(List<PageInfo> pages, string code, string offset, int limit, int ns)
        {
            return FillFromRecentChanges(pages, code, offset, limit, true, ns, false, true);
        }

        public static string FillFromRecentChanges(List<PageInfo> pages, string code, string offset, int limit, int ns)
        {
            return FillFromRecentChanges(pages, code, offset, limit, false, ns, true, false);
        }

        public static string FillFromRecentChanges(List<PageInfo> pages, string code, string offset, int limit, bool newPages, int ns, bool withComments)
        {
            return FillFromRecentChanges(pages, code, offset, limit, newPages, ns, withComments, false);
        }

        public static string FillFromRecentChanges(List<PageInfo> pages, string code, string offset, int limit, bool newPages, int ns, bool withComments,
                                            bool withBots)
        {
            string uri =
                string.Format(
                    "http://{0}.wikipedia.org/w/api.php?action=query&list=recentchanges&format=xml&rcstart={1}&rclimit={2}&rcnamespace={3}&rcprop=title|user|timestamp",
                    code,
                    offset,
                    limit,
                    ns);
            if (withComments)
                uri += "|comment";
            //     if (!string.IsNullOrEmpty(offset))
            //         uri += "&rccontinue=" + offset;
            if (newPages)
                uri += "&rctype=new";
            if (!withBots)
                uri += "&rcshow=!bot";
            else
                uri += "&rcshow=!redirect";

            string newoffset = "";
            Console.WriteLine("Try to get {0} pages from recent changes", limit);
            string xmlText = WebPageDownload(uri);
            try
            {
                XmlDocument revItem = new XmlDocument();
                revItem.LoadXml(xmlText);
                XmlNodeList lpages = revItem.DocumentElement.SelectNodes("//rc");
                foreach (XmlNode lpage in lpages)
                {
                    if (lpage.Attributes["title"] == null)
                        continue;
                    var ttitle = lpage.Attributes["title"].Value.Trim();
                    var tuser = "";
                    if (lpage.Attributes["user"] != null)
                        tuser = lpage.Attributes["user"].Value.Trim();
                    var tcomment = "";
                    if (withComments && lpage.Attributes["comment"] != null)
                        tcomment = lpage.Attributes["comment"].Value.Trim();
                    var p = new PageInfo
                        {
                            Title = ttitle,
                            User = tuser,
                            Comment = tcomment
                        };
                    if (lpage.Attributes["timestamp"] != null)
                    {
                        DateTime ttimestamp;
                        if (DateTime.TryParse(lpage.Attributes["timestamp"].Value.Trim(), out ttimestamp))
                            p.Timestamp = ttimestamp;
                    }
                    pages.Add(p);
                }
                XmlNode cont = revItem.DocumentElement.SelectSingleNode("//query-continue/recentchanges");
                if (cont != null)
                    newoffset = cont.Attributes["rccontinue"].Value.Trim();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return newoffset;
            }
            Console.WriteLine("Total found {0} pages", pages.Count);
            return newoffset;
        }

        public static void FillFromNewPagesApi(List<PageInfo> pages, string code, DateTime startTime, DateTime endTime, int ns, int limit, bool timeFiltration)
        {
            var offset = endTime.ToString("yyyyMMddHHmmss");
            while (true)
            {
                offset = FillFromNewPagesApi(pages, code, offset, limit, ns);
                var pos = offset.IndexOf("|");
                if (pos > 0)
                    offset = offset.Remove(pos).Trim();
                if (GetTimeFromOffset(offset) < startTime)
                    break;
            }

            if (timeFiltration)
            {
                foreach (var p in pages.Where(p => p.Timestamp < startTime).ToList())
                {
                    pages.Remove(p);
                }
                Console.WriteLine("Pages after time filtration: {0}", pages.Count);
            }
        }

        public static void FillFromNewPagesApi(List<PageInfo> pages, string code, int days, int hours, int ns, int limit, bool timeFiltration)
        {
            var totalhours = hours + days * 24;
            var endTime = DateTime.UtcNow;
            var startTime = endTime.AddHours(-totalhours);

            FillFromNewPagesApi(pages, code, startTime, endTime, ns, limit, timeFiltration);
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

        public static string FillFromUserContributionsApi(List<PageInfo> pages, string code, string offset, string username, int limit, int ns)
        {
            string propuser = username;

            string uri =
                string.Format(
                    "http://{0}.wikipedia.org/w/api.php?action=query&format=xml&list=usercontribs&ucstart={1}&ucuser={2}&uclimit={3}&ucnamespace={4}",
                    code,
                    offset,
                    propuser,
                    limit,
                    ns);

            string newoffset = "";
            Console.WriteLine("Try to get {0} pages from user contributions", limit);
            string xmlText = WebPageDownload(uri);
            try
            {
                XmlDocument revItem = new XmlDocument();
                revItem.LoadXml(xmlText);
                XmlNodeList lpages = revItem.DocumentElement.SelectNodes("//item");
                foreach (XmlNode lpage in lpages)
                {
                    if (lpage.Attributes["title"] == null)
                        continue;
                    var ttitle = lpage.Attributes["title"].Value.Trim();
                    var tuser = "";
                    if (lpage.Attributes["user"] != null)
                        tuser = lpage.Attributes["user"].Value.Trim();
                    var tcomment = "";
                    if (lpage.Attributes["comment"] != null)
                        tcomment = lpage.Attributes["comment"].Value.Trim();
                    var p = new PageInfo
                    {
                        Title = ttitle,
                        User = tuser,
                        Comment = tcomment
                    };
                    var res = DateTime.TryParse(lpage.Attributes["timestamp"].Value.Trim(), out p.Timestamp);
                    if (res)
                        p.Timestamp = p.Timestamp.ToUniversalTime();
                    pages.Add(p);
                }
                XmlNode cont = revItem.DocumentElement.SelectSingleNode("//query-continue/usercontribs");
                if (cont != null)
                    newoffset = cont.Attributes["ucstart"].Value.Trim();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return newoffset;
            }
            Console.WriteLine("Total found {0} pages", pages.Count);
            return newoffset;
        }

        public static string FillFromAllPagesApi(List<PageInfo> pages, string code, string fromStr, int limit, int ns)
        {
            string uri =
                string.Format("http://{0}.wikipedia.org/w/api.php?action=query&format=xml&list=allpages&apfrom={1}&aplimit={2}&apnamespace={3}&apfilterredir=nonredirects",
                              code,
                              fromStr,
                              limit,
                              ns);

            string newoffset = "";
            Console.WriteLine("Try to get {0} pages from all pages", limit);
            string xmlText = WebPageDownload(uri);
            try
            {
                XmlDocument revItem = new XmlDocument();
                revItem.LoadXml(xmlText);
                XmlNodeList lpages = revItem.DocumentElement.SelectNodes("//p");
                foreach (XmlNode lpage in lpages)
                {
                    if (lpage.Attributes["title"] == null)
                        continue;
                    var ttitle = lpage.Attributes["title"].Value.Trim();
                    var p = new PageInfo
                    {
                        Title = ttitle
                    };
                    pages.Add(p);
                }
                XmlNode cont = revItem.DocumentElement.SelectSingleNode("//query-continue/allpages");
                if (cont != null)
                    newoffset = cont.Attributes["apcontinue"].Value.Trim();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            return newoffset;
        }
    }
}
