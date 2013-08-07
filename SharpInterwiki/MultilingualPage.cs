using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using DotNetWikiBot;
using SharpWikiApiFunctions;

namespace SharpInterwiki
{
    class InterwikiItem
    {
        public string Title;
        public string Code;
        public bool IsRedirect = false;
        public string RedirectTo = "";
        public bool IsExcluded = false;
        public bool IsChecked = false;
        public bool IsWdChecked = false;
        public bool IsLocalIwChecked = false;
        public bool IsOnWd = false;
        public bool IsToSection = false;
        public PageInfo Info;
    }

    class MultilingualPage
    {
        public List<InterwikiItem> Interwikis = new List<InterwikiItem>();
        public bool IsOnWikiData = false;
        public string WikiDataItem;
        public bool HasConflict = false;
        public string ConflictDescription = "";

    }

    class MultilingualPageList
    {
        private BotConfiguration botConfiguration;
        public Dictionary<string, Site> sites = new Dictionary<string, Site>();
        private const int MaxItNum = 5;
        private int _minInterwikiNumber = 2;
        private Site _wikidataSite;
        private int[] _portionSizes = new[] { 50, 20, 10, 5, 1 };
   //     private int[] _portionSizes = new[] { 5, 2, 1 };
        private int _bigPorionSize = 500;
        private const int _iterationLimit = 500;
        private Dictionary<string, Dictionary<string, string>> _wikiCodes;
        
        private readonly InterwikiLogger _commonlog;
        private readonly InterwikiLogger _actionlog;
        private readonly InterwikiLogger _conflictlog;

        public MultilingualPageList(BotConfiguration bc)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            botConfiguration = bc;

            _commonlog = new InterwikiLogger(botConfiguration.CommonLog, botConfiguration.CommonLogLevel);
            _actionlog = new InterwikiLogger(botConfiguration.ActionLog, botConfiguration.ActionLogLevel);
            _conflictlog = new InterwikiLogger(botConfiguration.ConflictLog, botConfiguration.ConflictLogLevel);
        }

        public Site LogIn()
        {
            const string siteName = "http://wikidata.org";

            int it = 0;
            while (it < MaxItNum)
            {
                try
                {
                    _wikidataSite = new Site(siteName, Settings.Default.Login, Settings.Default.Password);
                    _commonlog.LogData("Logged to Wikidata", 2);
                    return _wikidataSite;
                }
                catch (Exception e)
                {
                    _commonlog.LogData(e.ToString(), 2);
                    it++;
                    Thread.Sleep(10000 * (int)Math.Pow(2, it));
                }
            }
            _commonlog.LogData("Cannot login", 2);
            throw new ApplicationException("Cannot log in");
        }

        public void ProcessNewPages(string langcode, int ns, int hours, int query, bool fullcheck)
        {
            _commonlog.LogData("Starting a new page processing", 4);
            _commonlog.LogData("Language:", langcode, 4);
            _commonlog.LogData("Hours:", hours.ToString(), 4);
            _commonlog.LogData("Query size:", query.ToString(), 4);

            var pages = new List<PageInfo>();

            WikiApiFunctions.FillFromNewPagesApi(pages, langcode, 0, hours, ns, 500, true);
            _commonlog.LogData("Found new page(s)", pages.Count.ToString(), 4);
            if (pages.Count <= 0)
            {
                _commonlog.LogData("Finishing", 4);
                return;
            }

            LogIn();
            _wikiCodes = WikiApiFunctions.GetWikiCodes();
            
            int portionNumber = (pages.Count - 1)/500 + 1;
            for (int i = 0; i < portionNumber; i++)
            {
                var firstind = i*500;
                var num = pages.Count - firstind;
                if (num > 500)
                    num = 500;
                _commonlog.LogData("Check portion of pages", 2);
                _commonlog.LogData("Pages in portion:", num.ToString(), 2);
                ProcessPortion(langcode, pages.Skip(firstind).Take(num).Select(p => p.Title).ToList(), fullcheck);
            }
        }

        public void ProcessRangePages(string langcode, int ns, string fromStr, string toStr, int query, bool fullcheck)
        {
            LogIn();

            if (string.IsNullOrEmpty(fromStr))
                fromStr = "!";
            if (string.IsNullOrEmpty(toStr))
                toStr = "";
            
            fromStr = HttpUtility.UrlDecode(fromStr).Replace('_', ' ');
            toStr = HttpUtility.UrlDecode(toStr).Replace('_', ' ');
            _wikiCodes = WikiApiFunctions.GetWikiCodes();

            var pages = new List<PageInfo>();

            while (true)
            {
                pages.Clear();
                var newFromStr = WikiApiFunctions.FillFromAllPagesApi(pages, langcode, fromStr, query, ns);
                int portionNumber = (pages.Count - 1) / _bigPorionSize + 1;
                for (int i = 0; i < portionNumber; i++)
                {
                    var firstind = i * _bigPorionSize;
                    var num = pages.Count - firstind;
                    if (num > _bigPorionSize)
                        num = _bigPorionSize;
                    ProcessPortion(langcode, pages.Skip(firstind).Take(num).Select(p => p.Title).ToList(), fullcheck);
                }
                
                if (newFromStr.Length == 0)
                    break;
                if (toStr.Length > 0 && newFromStr.CompareTo(toStr) > 0)
                    break;
                fromStr = newFromStr;
            }
        }

        public void ProcessPage(string langcode, int ns, string title, bool fullcheck)
        {
            if (string.IsNullOrEmpty(title))
            {
                _commonlog.LogData("Incorrect page title. Finishing.", 5);
                return;
            }
            LogIn();

            title = HttpUtility.UrlDecode(title).Replace('_', ' ');
            _wikiCodes = WikiApiFunctions.GetWikiCodes();
            
            ProcessPortion(langcode, new List<string> { title }, fullcheck);
        }


        private void ProcessPortion(string langcode, List<string> pages, bool fullcheck)
        {
            var mlpl = new List<MultilingualPage>();
            foreach (var page in pages)
            {
                var mlp = new MultilingualPage();
                mlp.Interwikis.Add(new InterwikiItem {Title = page, Code = langcode});
                mlpl.Add(mlp);
            }
            _commonlog.LogData("First page in the portion:", pages[0], 2);
            var logstring = string.Format("Iteration {0}. Check {1} links of language {2}...", 0, pages.Count, langcode);
            _commonlog.LogData(logstring, 1);
            GetProperTitles(mlpl, langcode);

            GetWikidataLinks(mlpl, langcode);
            foreach (var mlp in mlpl.Where(mlp => mlp.IsOnWikiData))
            {
                foreach (var iw in mlp.Interwikis.Where(iw => iw.Code == langcode))
                {
                    Console.WriteLine("Page {0}:{1} has WikiData item {2}", langcode, iw.Title, mlp.WikiDataItem);
                }
            }
            // Excluding pages with WikiData item
            if (!fullcheck)
                mlpl = mlpl.Where(mlp => !mlp.IsOnWikiData).ToList();

            GetLocalInterwiki(mlpl, langcode);
            foreach (var mlp in mlpl.Where(mlp => !mlp.IsOnWikiData && mlp.Interwikis.All(iw => iw.Code == langcode)))
            {
                foreach (var iw in mlp.Interwikis.Where(iw => iw.Code == langcode))
                {
                    Console.WriteLine("Page {0}:{1} has no local links", langcode, iw.Title);
                }
            }
            // Excluding pages without local interwikis
            mlpl = mlpl.Where(mlp => mlp.Interwikis.Any(iw => iw.Code != langcode)).ToList();
            if (mlpl.Count == 0)
            {
                _commonlog.LogData("Nothing to check", 2);
                return;
            }
            _commonlog.LogData("Groups to check:", mlpl.Count.ToString(), 1);

            for (int it = 1; it < _iterationLimit; it++) 
            {
                var langGroups = mlpl.Where(mlp => !mlp.HasConflict)
                                     .SelectMany(mlp => mlp.Interwikis)
                                     .Where(iw => !iw.IsRedirect && !iw.IsExcluded && !iw.IsToSection
                                                  && (!iw.IsChecked || !iw.IsWdChecked || !iw.IsLocalIwChecked))
                                     .GroupBy(iw => iw.Code);
                if (!langGroups.Any())
                    break;
                var primaryGroup = langGroups.OrderByDescending(g => g.Count()).First();
                var primaryCode = primaryGroup.Key;
                var primaryArray = primaryGroup.Select(iw => iw.Title).Distinct().ToArray();
                logstring = string.Format("Iteration {0}. Check {1} links of language {2}...", it, primaryArray.Length, primaryCode);
                _commonlog.LogData(logstring, 1);

                GetProperTitles(mlpl, primaryCode);
                GetWikidataLinks(mlpl, primaryCode);
                GetLocalInterwiki(mlpl, primaryCode);

                CheckConflicts(mlpl);
                CheckNamespaces(mlpl, primaryCode);
                if (it >= 499)
                {
                    foreach (var mlp in mlpl)
                    {
                        var uncheckedIw =
                            mlp.Interwikis.FirstOrDefault(iw => iw.Code == primaryCode && primaryArray.Contains(iw.Title));
                        if (uncheckedIw == null)
                            continue;
                        mlp.HasConflict = true;
                        mlp.ConflictDescription =
                            string.Format("Too much iterations. Started on {0}:{1}. Looped on {2}:{3}",
                                          langcode,
                                          mlp.Interwikis[0].Title,
                                          primaryCode,
                                          uncheckedIw.Title);
                    }
                }
            }

            var mlpConflicts = mlpl.Where(mlp => mlp.HasConflict).ToList();
            var mlpUpdates = mlpl.Where(mlp => !mlp.HasConflict && mlp.IsOnWikiData).ToList();
            var mlpCreates = mlpl.Where(mlp => !mlp.HasConflict && !mlp.IsOnWikiData && mlp.Interwikis.Count > 1).ToList();
            if (mlpConflicts.Count > 0)
            {
                _commonlog.LogData("Conflicts:", mlpConflicts.Count.ToString(), 1);
                foreach (var mlp in mlpConflicts)
                {
                    _conflictlog.LogData(mlp.ConflictDescription, 2);
                }
            }
            if (mlpUpdates.Count > 0)
            {
                _commonlog.LogData("Proposed updates:", mlpUpdates.Count.ToString(), 1);
            }
            if (mlpCreates.Count > 0)
            {
                _commonlog.LogData("Proposed creations:", mlpCreates.Count.ToString(), 1);
            }
            foreach (var mlpCreate in mlpCreates)
            {
                var iwList = mlpCreate
                    .Interwikis
                    .Where(iw => !iw.IsRedirect && !iw.IsExcluded && !iw.IsToSection)
                    .ToDictionary(iw => iw.Code, iw => iw.Title);
                if (iwList.Count < _minInterwikiNumber) 
                {
                    Console.WriteLine("Page {0}:{1} has not sufficient number of interwikis. Skipping...",
                        iwList.ElementAt(0).Key, iwList.ElementAt(0).Value);
                    continue;
                }
                Item wdItem = new Item(_wikidataSite);
                try
                {
                    wdItem.createItem(iwList, "Creation of a new item");
                    logstring = string.Format("Created item Q{0} with {1} interwikis: ", wdItem.id, iwList.Count);
                    if (iwList.Count < 10)
                    {
                        logstring = iwList.Aggregate(logstring,
                                                     (current, iw) =>
                                                     current + string.Format("{0}:{1}, ", iw.Key, iw.Value));
                        logstring = logstring.TrimEnd(new[] { ' ', ',' });
                    }
                    else
                        logstring += string.Join(", ", iwList.Keys);
                    _actionlog.LogData(logstring, 3);
                }
                catch (Exception e)
                {
                    if (iwList.ContainsKey(langcode))
                    {
                        logstring = string.Format("Problem when page {0} creation", iwList[langcode]);
                        _actionlog.LogData(logstring, 3);
                    }
                    _actionlog.LogData(e.ToString(), 3);
                }
            }
            foreach (var mlpUpdate in mlpUpdates)
            {
                var properIws = mlpUpdate
                    .Interwikis
                    .Where(iw => !iw.IsRedirect && !iw.IsExcluded && !iw.IsToSection);
                if (properIws.All(iw => iw.IsOnWd))
                {
                    Console.WriteLine("Page {0}:{1} has not sufficient additional of interwikis. Skipping...",
                        properIws.ElementAt(0).Code, properIws.ElementAt(0).Title);
                    continue;
                }
                var iwList = properIws
                    .ToDictionary(iw => iw.Code, iw => iw.Title);
                Item wdItem = new Item(_wikidataSite, mlpUpdate.WikiDataItem);
                wdItem.Load();
                var codes = _wikiCodes["wiki"];
                var oldIwList = wdItem.links.Where(iw => codes.ContainsKey(iw.Key))
                                      .ToDictionary(iw => codes[iw.Key], iw => iw.Value);
                var diffList = iwList.Where(iw => !oldIwList.ContainsKey(iw.Key))
                                     .ToDictionary(iw => iw.Key, iw => iw.Value);
                try
                {
                    wdItem.setSiteLink(iwList, "Adding of language links");
                    logstring = string.Format("Added {1} interwiki(s) to item Q{0}: ", wdItem.id, diffList.Count);
                    if (diffList.Count < 5)
                    {
                        logstring = diffList.Aggregate(logstring, (current, iw) => current + string.Format("{0}:{1}, ", iw.Key, iw.Value));
                        logstring = logstring.TrimEnd(new[] { ' ', ',' });
                    }
                    else
                        logstring += string.Join(", ", diffList.Keys);
                    _actionlog.LogData(logstring, 3);
                }
                catch (Exception e)
                {
                    if (iwList.ContainsKey(langcode))
                    {
                        logstring = string.Format("Problem when link {0} adding", iwList[langcode]);
                        _actionlog.LogData(logstring, 3);
                    }
                    _actionlog.LogData(e.ToString(), 3);
                }
            }
        }

        private void GetProperTitles(List<MultilingualPage> mlpl, string code)
        {
            int curPos = 0;
            var titlesToCheck = mlpl.Where(mlp => !mlp.HasConflict)
                                    .SelectMany(mlp => mlp.Interwikis)
                                    .Where(iw => iw.Code == code && !iw.IsRedirect
                                                 && !iw.IsToSection && !iw.IsExcluded && !iw.IsChecked)
                                    .Select(iw => iw.Title)
                                    .ToArray();
            while (curPos < titlesToCheck.Length)
            {
                for (int i = 0; i < _portionSizes.Length; i++)
                {
                    int portionSize = _portionSizes[i];
                    int bdiff = titlesToCheck.Length - curPos;
                    if (portionSize > bdiff)
                        portionSize = bdiff;
                    var portionOfTitles = titlesToCheck.Skip(curPos).Take(portionSize).ToArray();
                    if(HttpUtility.UrlEncode(string.Join("|", portionOfTitles)).Length > 8000)
                        continue;
                    try
                    {
                        var logstring = string.Format("\t[{0}] Getting proper titles for {1} page(s)",
                            code,
                            portionOfTitles.Length);
                        _commonlog.LogData(logstring, 0);
                        GetProperTitles(mlpl, code, portionOfTitles);
                        curPos += portionSize;
                        break;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
            }
        }

        private void GetProperTitles(List<MultilingualPage> mlpl, string code, string[] titles)
        {
            var pages = WikiApiFunctions.GetProperTitles(code, titles);          
            foreach (var title in titles)
            {
                var page = pages.FirstOrDefault(p => p.Title == title);
                if (page == null) 
                    page = WikiApiFunctions.GetProperTitle(code, title);
                foreach (var mlp in mlpl)
                {
                    var interwiki = mlp.Interwikis.FirstOrDefault(iw => iw.Code == code && iw.Title == title);
                    if (interwiki == null) 
                        continue;
                    interwiki.Info = page;
                    interwiki.IsExcluded = page.IsMissing;
                    interwiki.IsChecked = true;
                    if (page.RedirectTo.Length <= 0 && page.NormalizedTitle.Length <= 0) 
                        continue;

                    interwiki.IsRedirect = true;
                    var redirectedIw = new InterwikiItem {Code = code};
                    if (page.RedirectTo.Length > 0)
                    {
                        interwiki.RedirectTo = page.RedirectTo;
                        if (page.RedirectionSection.Length > 0)
                            interwiki.RedirectTo += "#" + page.RedirectionSection;
                        redirectedIw.Title = page.RedirectTo;
                    }
                    else
                    {
                        interwiki.RedirectTo = page.NormalizedTitle;
                        redirectedIw.Title = page.NormalizedTitle;
                    }

                    if(mlp.Interwikis.Any(iw => iw.Code == code && iw.Title == redirectedIw.Title))
                        continue;

                    var redirectedPage = pages.FirstOrDefault(p => p.Title == redirectedIw.Title);
                    if (redirectedPage == null)
                    {
                        redirectedIw.IsExcluded = true;
                    }
                    else
                    {
                        redirectedIw.Info = redirectedPage;
                        redirectedIw.IsExcluded = redirectedPage.IsMissing;
                    }
                    redirectedIw.IsRedirect = false;
                    redirectedIw.IsChecked = true;


                    mlp.Interwikis.Add(redirectedIw);
                }
            }
        }

        private void GetWikidataLinks(List<MultilingualPage> mlpl, string code)
        {
            int curPos = 0;
            var titlesToCheck = mlpl.Where(mlp => !mlp.HasConflict)
                                    .SelectMany(mlp => mlp.Interwikis)
                                    .Where(iw => iw.Code == code && !iw.IsRedirect
                                                 && !iw.IsToSection && !iw.IsExcluded && !iw.IsWdChecked)
                                    .Select(iw => iw.Title)
                                    .ToArray();
            while (curPos < titlesToCheck.Length)
            {
                for (int i = 0; i < _portionSizes.Length; i++)
                {
                    int portionSize = _portionSizes[i];
                    int bdiff = titlesToCheck.Length - curPos;
                    if (portionSize > bdiff)
                        portionSize = bdiff;
                    var portionOfTitles = titlesToCheck.Skip(curPos).Take(portionSize).ToArray();
                    if (HttpUtility.UrlEncode(string.Join("|", portionOfTitles)).Length > 8000)
                        continue;
                    try
                    {
                        var logstring = string.Format("\t[{0}] Getting Wikidata links for {1} page(s)",
                            code,
                            portionOfTitles.Length);
                        _commonlog.LogData(logstring, 0);
                        GetWikidataLinks(mlpl, code, portionOfTitles);
                        curPos += portionSize;
                        break;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
            }
        }

        private void GetWikidataLinks(List<MultilingualPage> mlpl, string code, string[] titles)
        {
            var wikiDataLinks = WikiApiFunctions.GetWikidataLinks(code, titles, true);
            Dictionary<string, string> codes;
            if (_wikiCodes.ContainsKey("wiki"))
                codes = _wikiCodes["wiki"];
            else
            {
                codes = new Dictionary<string, string>();
                _conflictlog.LogData("WTF???", 4);
            }

            foreach (var title in titles)
            {
                foreach (var interwikiItem in mlpl.SelectMany(mlp => mlp.Interwikis).Where(iw => iw.Title == title && iw.Code == code).ToList())
                {
                    interwikiItem.IsWdChecked = true;
                }
            }
           
            foreach (var wdEntities in wikiDataLinks)
            {
                if(wdEntities.Count == 0)
                    continue;
                var curProjectLinks = wdEntities.Keys.Where(codes.ContainsKey).ToDictionary(lc => codes[lc], lc => wdEntities[lc]);
                curProjectLinks.Add("wikidata", wdEntities["wikidata"]);
                foreach (var mlp in mlpl)
                {
                    if (!mlp.Interwikis.Any(iw => iw.Title == curProjectLinks[code] && iw.Code == code))
                        continue;
                    var wdId = wdEntities["wikidata"];
                    if (mlp.IsOnWikiData && mlp.WikiDataItem != wdId)
                    {
                        mlp.HasConflict = true;
                        mlp.ConflictDescription =
                            string.Format("Starting from: {0}:{1}. Found items on WikiData: {2} and {3}",
                                          mlp.Interwikis[0].Code,
                                          mlp.Interwikis[0].Title, mlp.WikiDataItem, wdId);
                        continue;
                    }
                    mlp.IsOnWikiData = true;
                    mlp.WikiDataItem = wdId;
                    foreach (var wdIw in curProjectLinks)
                    {
                        if(wdIw.Key == "wikidata")
                            continue;
                        if(mlp.Interwikis.Any(iw => iw.Code == wdIw.Key && iw.Title == wdIw.Value))
                            continue;
                        mlp.Interwikis.Add(new InterwikiItem {Code = wdIw.Key, Title = wdIw.Value});
                    }
                    foreach (var iw in mlp.Interwikis.Where(iw => curProjectLinks.ContainsKey(iw.Code) && curProjectLinks[iw.Code] == iw.Title).ToList())
                    {
                        iw.IsWdChecked = true;
                        iw.IsOnWd = true;
                    }
                }
            }
        }

        private void GetLocalInterwiki(List<MultilingualPage> mlpl, string code)
        {
            int curPos = 0;
            var titlesToCheck = mlpl.Where(mlp => !mlp.HasConflict)
                                    .SelectMany(mlp => mlp.Interwikis)
                                    .Where(iw => iw.Code == code && !iw.IsRedirect
                                                 && !iw.IsToSection && !iw.IsExcluded && !iw.IsLocalIwChecked)
                                    .Select(iw => iw.Title)
                                    .ToArray();

            while (curPos < titlesToCheck.Length)
            {
                for (int i = 0; i < _portionSizes.Length; i++)
                {
                    int portionSize = _portionSizes[i];
                    int bdiff = titlesToCheck.Length - curPos;
                    if (portionSize > bdiff)
                        portionSize = bdiff;
                    var portionOfTitles = titlesToCheck.Skip(curPos).Take(portionSize).ToArray();
                    if (HttpUtility.UrlEncode(string.Join("|", portionOfTitles)).Length > 8000)
                        continue;
                    try
                    {
                        var logstring = string.Format("\t[{0}] Getting local interlanuage links for {1} page(s)",
                            code,
                            portionOfTitles.Length);
                        _commonlog.LogData(logstring, 0);
                        GetLocalInterwiki(mlpl, code, portionOfTitles);
                        curPos += portionSize;
                        break;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
            }
        }

        private void GetLocalInterwiki(List<MultilingualPage> mlpl, string code, string[] titles)
        {
            var allPagesInterwikis = WikiApiFunctions.GetLocalInterwiki(code, titles);
            var codes = _wikiCodes["wiki"];

            foreach (var mlp in mlpl)
            {
                foreach (var mlpInterwiki in mlp.Interwikis.Where(iw => iw.Code == code && allPagesInterwikis.ContainsKey(iw.Title)).ToList())
                {
                    mlpInterwiki.IsLocalIwChecked = true;
                    var localPageInterwikis = allPagesInterwikis[mlpInterwiki.Title];

                    foreach (var iw in localPageInterwikis.Where(iw => !mlp.Interwikis.Any(mlpIw => mlpIw.Code == iw.Key && mlpIw.Title == iw.Value)).ToList())
                    {
                        if(codes.All(p => p.Value != iw.Key))
                            continue;
                        mlp.Interwikis.Add(new InterwikiItem
                            {
                                Code = iw.Key,
                                Title = iw.Value,
                                IsToSection = iw.Value.Contains("#")
                            });
                    }
                }
                //Esperanto API bug
                if(code == "eo")
                    foreach (var mlpInterwiki in mlp.Interwikis.Where(iw => iw.Code == "eo" && !iw.IsLocalIwChecked && titles.Contains(iw.Title)).ToList())
                    {
                        mlpInterwiki.IsLocalIwChecked = true;
                    }
            }
        }

        private void CheckConflicts(List<MultilingualPage> mlpl)
        {
            foreach (var mlp in mlpl.Where(mlp => !mlp.HasConflict))
            {
                var langGroups =
                    mlp.Interwikis
                       .Where(iw => !iw.IsExcluded && !iw.IsRedirect && iw.IsChecked)
                       .GroupBy(iw => iw.Code)
                       .OrderByDescending(g => g.Count());
                if(!langGroups.Any())
                    continue;
                var maxLangGroup = langGroups.First();
                if (maxLangGroup.Count() <= 1) 
                    continue;
                mlp.HasConflict = true;
                mlp.ConflictDescription =
                    string.Format("Starting from: {0}:{1}. Found different local pages: {2}:{3} and {2}:{4}",
                                  mlp.Interwikis[0].Code,
                                  mlp.Interwikis[0].Title,
                                  maxLangGroup.Key,
                                  maxLangGroup.ElementAt(0).Title,
                                  maxLangGroup.ElementAt(1).Title);
            }
        }

        private void CheckNamespaces(List<MultilingualPage> mlpl, string code)
        {
            foreach (var mlp in mlpl.Where(mlp => !mlp.HasConflict))
            {
                var firstLinkInfo = mlp.Interwikis[0].Info;
                var properNamespace = -1;
                if (firstLinkInfo != null)
                    properNamespace = firstLinkInfo.Namespace;
                if (properNamespace < 0) 
                    continue;
               
                var interwiki = mlp.Interwikis.Where(iw => iw.Code == code && iw.IsChecked).ToList();
                foreach (var iw in interwiki)
                {
                    if (iw.Info == null
                        || !CheckNamespaceConfirmity(
                            iw.Code,
                            iw.Info.Namespace,
                            mlp.Interwikis[0].Code,
                            firstLinkInfo.Namespace)) 
                    {
                        iw.IsExcluded = true;
                        if (!mlp.Interwikis[0].IsOnWd && iw.IsOnWd && iw.Info != null && iw.Info.Namespace >= 0)
                        {
                            mlp.HasConflict = true;
                            mlp.ConflictDescription =
                                string.Format("Namespace of page {0} is not corresponded to wikidata item {1}",
                                              mlp.Interwikis[0].Title, 
                                              mlp.WikiDataItem);
                        }
                    }
                }
            }
        }

        private bool CheckNamespaceConfirmity(string lang1, int ns1, string lang2, int ns2)
        {
            int oddity;
            Math.DivRem(ns1, 2, out oddity);
            if (oddity == 1)
                return false;
            Math.DivRem(ns2, 2, out oddity);
            if (oddity == 1)
                return false;
            if (ns1 == 2 || ns2 == 2)
                return false;

            if (ns1 == ns2)
                return true;

            return botConfiguration.CheckNamespaceConfirmity(lang1, ns1, lang2, ns2);
        }


    }
}
