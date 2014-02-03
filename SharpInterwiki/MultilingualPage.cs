using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
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

    partial class MultilingualPageList
    {
        private readonly BotConfiguration _botConfiguration;
        //      public Dictionary<string, Site> sites = new Dictionary<string, Site>();
        private const int MaxItNum = 5;
        private Site _wikidataSite;
        //     private int[] _portionSizes = new[] { 5, 2, 1 };
        private int _bigPortionSize = 500;
        private const int IterationLimit = 500;
        private LanguageCodes _wikiCodes;

        private readonly Regex _innerLinkRegex = new Regex(@"\[\[([^\]]*)\]\]");

        private readonly InterwikiLogger _commonlog;
        private readonly InterwikiLogger _actionlog;
        private readonly InterwikiLogger _conflictlog;

        public MultilingualPageList(BotConfiguration bc, InputParameters inp)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            _botConfiguration = bc;

            _commonlog = new InterwikiLogger(_botConfiguration.CommonLog, _botConfiguration.CommonLogLevel, inp);
            _actionlog = new InterwikiLogger(_botConfiguration.ActionLog, _botConfiguration.ActionLogLevel, inp);
            _conflictlog = new InterwikiLogger(_botConfiguration.ConflictLog, _botConfiguration.ConflictLogLevel, inp);
        }

        public Site LogIn()
        {
            const string siteName = "https://wikidata.org";

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

        public void ProcessNewPages(InputParameters inp)
        {
            _commonlog.LogData("", 5);
            _commonlog.LogData("New page processing", 4);
            _commonlog.LogData("Language:", inp.Langcode, 4);
            _commonlog.LogData("Namespace:", inp.Ns.ToString(), 4);
            _commonlog.LogData("Hours:", inp.Hours.ToString(), 4);
            _commonlog.LogData("Query size:", inp.Query.ToString(), 4);
            if (inp.Fullcheck)
                _commonlog.LogData("Full check", 4);

            var pages = new List<PageInfo>();
            Dictionary<string, object> queryParams = new Dictionary<string, object>
                {
                    {"querytype", "newpages"},
                    {"ns", inp.Ns},
                    {"limit", inp.Query},
                    {"withcomments", 1},
                    {"hours", inp.Hours},
                    {"timefiltration", 1}
                };

            WikiApiFunctions.FillFromApiQuery(pages, inp.Langcode, inp.Projectcode, queryParams);
            _commonlog.LogData("Found new page(s):", pages.Count.ToString(), 4);
            if (pages.Count <= 0)
            {
                _commonlog.LogData("Finishing", 4);
                return;
            }

            LogIn();
            _wikiCodes = _botConfiguration.GetWikiCodes(inp.Projectcode);

            int portionNumber = (pages.Count - 1) / 500 + 1;
            for (int i = 0; i < portionNumber; i++)
            {
                var firstind = i * 500;
                var num = pages.Count - firstind;
                if (num > 500)
                    num = 500;
                _commonlog.LogData("", 2);
                _commonlog.LogData("New portion of pages", 2);
                _commonlog.LogData("Number of pages:", num.ToString(), 2);
                TryProcessPortion(inp.Langcode,
                               inp.Projectcode,
                               pages.Skip(firstind).Take(num).Select(p => p.Title).ToList(),
                               inp.Fullcheck,
                               inp.OnlyUpdate);
            }
        }

        public void ProcessRangePages(InputParameters inp)
        {
            LogIn();
            var fromStr = inp.Fromstr;
            var toStr = inp.Tostr;

            if (string.IsNullOrEmpty(fromStr))
                fromStr = "!";
            if (string.IsNullOrEmpty(toStr))
                toStr = "";

            fromStr = HttpUtility.UrlDecode(fromStr).Replace('_', ' ');
            toStr = HttpUtility.UrlDecode(toStr).Replace('_', ' ');
            _wikiCodes = _botConfiguration.GetWikiCodes(inp.Projectcode);

            _commonlog.LogData("", 5);
            _commonlog.LogData("Range processing", 4);
            _commonlog.LogData("Language:", inp.Langcode, 4);
            _commonlog.LogData("Namespace:", inp.Ns.ToString(), 4);
            _commonlog.LogData("Starting:", fromStr, 4);
            if (toStr.Length > 0)
                _commonlog.LogData("Ending:", toStr, 4);
            if (inp.Fullcheck)
                _commonlog.LogData("Full check", 4);

            var pages = new List<PageInfo>();

            while (true)
            {
                pages.Clear();
                Dictionary<string, object> queryParams = new Dictionary<string, object>
                {
                    {"querytype", "allpages"},
                    {"ns", inp.Ns},
                    {"limit", inp.Query},
                    {"offset", fromStr}
                };
                var newFromStr = WikiApiFunctions.FillFromApiQuery(pages, inp.Langcode, inp.Projectcode, queryParams);
                int portionNumber = (pages.Count - 1) / _bigPortionSize + 1;
                for (int i = 0; i < portionNumber; i++)
                {
                    var firstind = i * _bigPortionSize;
                    var num = pages.Count - firstind;
                    if (num > _bigPortionSize)
                        num = _bigPortionSize;
                    _commonlog.LogData("", 2);
                    _commonlog.LogData("New portion of pages", 2);
                    _commonlog.LogData("Number of pages:", num.ToString(), 2);
                    TryProcessPortion(inp.Langcode,
                                   inp.Projectcode,
                                   pages.Skip(firstind).Take(num).Select(p => p.Title).ToList(),
                                   inp.Fullcheck,
                                   inp.OnlyUpdate);
                }

                if (newFromStr.Length == 0)
                    break;
                if (toStr.Length > 0 && newFromStr.CompareTo(toStr) > 0)
                    break;
                fromStr = newFromStr;
            }
        }

        public void ProcessCategoryPages(InputParameters inp)
        {
            LogIn();

            var catname = inp.Catname;

            catname = HttpUtility.UrlDecode(catname).Replace('_', ' ');
            _wikiCodes = _botConfiguration.GetWikiCodes(inp.Projectcode);

            _commonlog.LogData("", 5);
            _commonlog.LogData("Category processing", 4);
            _commonlog.LogData("Language:", inp.Langcode, 4);
            _commonlog.LogData("Namespace:", inp.Ns.ToString(), 4);
            _commonlog.LogData("Category:", catname, 4);
            _commonlog.LogData("Depth:", inp.Depth.ToString(), 4);
            if (inp.Fullcheck)
                _commonlog.LogData("Full check", 4);

            var pages = new List<PageInfo>();

            pages.Clear();
            Dictionary<string, object> queryParams = new Dictionary<string, object>
                {
                    {"querytype", "categorytree"},
                    {"ns", inp.Ns},
                    {"limit", inp.Query},
                    {"categoryname", catname},
                    {"depth", inp.Depth}
                };
            WikiApiFunctions.FillFromApiQuery(pages, inp.Langcode, inp.Projectcode, queryParams);
            int portionNumber = (pages.Count - 1) / _bigPortionSize + 1;
            for (int i = 0; i < portionNumber; i++)
            {
                var firstind = i * _bigPortionSize;
                var num = pages.Count - firstind;
                if (num > _bigPortionSize)
                    num = _bigPortionSize;
                _commonlog.LogData("", 2);
                _commonlog.LogData("New portion of pages", 2);
                _commonlog.LogData("Number of pages:", num.ToString(), 2);
                TryProcessPortion(inp.Langcode,
                               inp.Projectcode,
                               pages.Skip(firstind).Take(num).Select(p => p.Title).ToList(),
                               inp.Fullcheck,
                               inp.OnlyUpdate);
            }
        }

        public void ProcessPage(InputParameters inp)
        {
            var title = inp.Fromstr;
            if (string.IsNullOrEmpty(title))
            {
                _commonlog.LogData("Incorrect page title. Finishing.", 5);
                return;
            }
            LogIn();

            title = HttpUtility.UrlDecode(title).Replace('_', ' ');
            _wikiCodes = _botConfiguration.GetWikiCodes(inp.Projectcode);

            _commonlog.LogData("", 5);
            _commonlog.LogData("One page processing", 4);
            _commonlog.LogData("Language:", inp.Langcode, 4);
            _commonlog.LogData("Namespace:", inp.Ns.ToString(), 4);
            if (inp.Fullcheck)
                _commonlog.LogData("Full check", 4);

            TryProcessPortion(inp.Langcode,
                           inp.Projectcode,
                           new List<string> { title },
                           inp.Fullcheck,
                           inp.OnlyUpdate);
        }

        public void ProcessUserContributions(InputParameters inp)
        {
            _commonlog.LogData("", 5);
            _commonlog.LogData("User contribution processing", 4);
            _commonlog.LogData("Language:", inp.Langcode, 4);
            _commonlog.LogData("User:", inp.User, 4);
            _commonlog.LogData("Namespace:", inp.Ns.ToString(), 4);
            _commonlog.LogData("Hours:", inp.Hours.ToString(), 4);
            _commonlog.LogData("Query size:", inp.Query.ToString(), 4);
            if (inp.Fullcheck)
                _commonlog.LogData("Full check", 4);

            var pages = new List<PageInfo>();
            Dictionary<string, object> queryParams = new Dictionary<string, object>
                {
                    {"querytype", "usercontributions"},
                    {"ns", inp.Ns},
                    {"limit", inp.Query},
                    {"timefiltration", 1}
                };

            WikiApiFunctions.FillFromApiQuery(pages, inp.Langcode, inp.Projectcode, queryParams);
            _commonlog.LogData("Found new page(s):", pages.Count.ToString(), 4);
            if (pages.Count <= 0)
            {
                _commonlog.LogData("Finishing", 4);
                return;
            }

            LogIn();
            _wikiCodes = _botConfiguration.GetWikiCodes(inp.Projectcode);

            int portionNumber = (pages.Count - 1) / 500 + 1;
            for (int i = 0; i < portionNumber; i++)
            {
                var firstind = i * 500;
                var num = pages.Count - firstind;
                if (num > 500)
                    num = 500;
                _commonlog.LogData("", 2);
                _commonlog.LogData("New portion of pages", 2);
                _commonlog.LogData("Number of pages:", num.ToString(), 2);
                TryProcessPortion(inp.Langcode,
                               inp.Projectcode,
                               pages.Skip(firstind).Take(num).Select(p => p.Title).ToList(),
                               inp.Fullcheck,
                               inp.OnlyUpdate);
            }
        }

        private bool TryProcessPortion(string langcode, string projectcode, List<string> pages, bool fullcheck, bool onlyupdate)
        {

            for (int trycount = 0; trycount < 5; trycount++)
            {
                try
                {
                    ProcessPortion(langcode, projectcode, pages, fullcheck, onlyupdate);
                    return true;
                }
                catch (Exception e)
                {
                    _commonlog.LogData("Error while the portion processing", 4);
                    _commonlog.LogData(e.Message, 4);
                    foreach (KeyValuePair<object, object> kv in e.Data)
                    {
                        _commonlog.LogData(kv.Key + ": " + kv.Value, 4);
                    }
                    _commonlog.LogData(e.StackTrace, 4);
                    _commonlog.LogData(e.Source, 4);
                    if (!(e is WebException))
                        return false;

                    Thread.Sleep(20000);
                    _commonlog.LogData("Next try", 4);
                }
            }
            return false;
        }

        private void ProcessPortion(string langcode, string projectcode, List<string> pages, bool fullcheck, bool onlyupdate)
        {
            var mlpl = new List<MultilingualPage>();
            foreach (var page in pages)
            {
                var mlp = new MultilingualPage();
                mlp.Interwikis.Add(new InterwikiItem { Title = page, Code = langcode });
                mlpl.Add(mlp);
            }

            _commonlog.LogData("First page in the portion:", pages[0], 2);
            var logstring = string.Format("Iteration {0}. Check {1} links of language {2}...", 0, pages.Count, langcode);
            _commonlog.LogData(logstring, 1);
            GetProperTitles(mlpl, langcode, projectcode);

            GetWikidataLinks(mlpl, langcode, projectcode);
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

            GetLocalInterwiki(mlpl, langcode, projectcode);
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

            for (int it = 1; it < IterationLimit; it++)
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

                GetProperTitles(mlpl, primaryCode, projectcode);
                GetWikidataLinks(mlpl, primaryCode, projectcode);
                GetLocalInterwiki(mlpl, primaryCode, projectcode);

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
            if (mlpCreates.Count > 0)
            {
                _commonlog.LogData("Proposed creations:", mlpCreates.Count.ToString(), 1);
                if (onlyupdate)
                    _commonlog.LogData("Creation prohibited", 1);
            }
            if (mlpUpdates.Count > 0)
            {
                _commonlog.LogData("Proposed updates:", mlpUpdates.Count.ToString(), 1);
            }
            if (mlpConflicts.Count > 0)
            {
                _commonlog.LogData("Conflicts:", mlpConflicts.Count.ToString(), 1);
                foreach (var mlp in mlpConflicts)
                {
                    _conflictlog.LogData(mlp.ConflictDescription, 2);
                }
            }

            foreach (var mlpCreate in mlpCreates)
            {
                if (projectcode != "wikipedia" || onlyupdate)
                    continue;
                var iwList = mlpCreate
                    .Interwikis
                    .Where(iw => !iw.IsRedirect && !iw.IsExcluded && !iw.IsToSection)
                    .ToDictionary(iw => iw.Code, iw => iw.Title);
                if (iwList.Count < _botConfiguration.MinInterwikiNumber)
                {
                    Console.WriteLine("Page {0}:{1} has not sufficient number of interwikis. Skipping...",
                        iwList.ElementAt(0).Key, iwList.ElementAt(0).Value);
                    continue;
                }
                Item wdItem = new Item(_wikidataSite);
                iwList = ReorderInterwiki(iwList);
                var projectIwList = _wikiCodes.ToProjectCodes(iwList);
                try
                {
                    wdItem.createItem(projectIwList, MakeCreationSummary(iwList, projectcode));
                    logstring = MakeActionLogString(iwList, projectcode);
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
                var iwList =
                    ReorderInterwiki(properIws
                                         .ToDictionary(iw => iw.Code, iw => iw.Title));
                Item wdItem = new Item(_wikidataSite, mlpUpdate.WikiDataItem);
                wdItem.Load();
                _wikiCodes.SetProjectCode(projectcode);
                var oldIwList = _wikiCodes.ToLanguageCodes(wdItem.links);
                var projectIwList = _wikiCodes.ToProjectCodes(iwList);

                var addList = _wikiCodes.MakeAddList(oldIwList, iwList);
                var updateList = _wikiCodes.MakeReplaceList(oldIwList, iwList);

                try
                {
                    wdItem.setSiteLink(projectIwList, MakeSummary(addList, updateList, projectcode, false));
                    logstring = MakeActionLogString(addList, updateList, projectcode, false);
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

        private void GetProperTitles(List<MultilingualPage> mlpl, string code, string project)
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
                for (int i = 0; i < _botConfiguration.Portions.Length; i++)
                {
                    int portionSize = _botConfiguration.Portions[i];
                    int bdiff = titlesToCheck.Length - curPos;
                    if (portionSize > bdiff)
                        portionSize = bdiff;
                    var portionOfTitles = titlesToCheck.Skip(curPos).Take(portionSize).ToArray();
                    if (HttpUtility.UrlEncode(string.Join("|", portionOfTitles)).Length > 8000)
                        continue;
                    try
                    {
                        var logstring = string.Format("\t[{0}] Getting proper titles for {1} page(s)",
                            code,
                            portionOfTitles.Length);
                        _commonlog.LogData(logstring, 0);
                        GetProperTitles(mlpl, code, project, portionOfTitles);
                        curPos += portionSize;
                        break;
                    }
                    catch (WikiApiException e)
                    {
                        Console.WriteLine(e);
                        if (e.Message != "Too large URI")
                            throw;
                        if (portionSize == 1)
                            curPos++;
                    }
                }
            }
        }

        private void GetProperTitles(List<MultilingualPage> mlpl, string code, string project, string[] titles)
        {
            var pages = WikiApiFunctions.GetProperTitles(code, project, titles);
            foreach (var title in titles)
            {
                var page = pages.FirstOrDefault(p => p.Title == title);
                if (page == null)
                    page = WikiApiFunctions.GetProperTitle(code, project, title);
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
                    var redirectedIw = new InterwikiItem { Code = code };
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

                    if (mlp.Interwikis.Any(iw => iw.Code == code && iw.Title == redirectedIw.Title))
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

        private void GetWikidataLinks(List<MultilingualPage> mlpl, string code, string project)
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
                for (int i = 0; i < _botConfiguration.Portions.Length; i++)
                {
                    int portionSize = _botConfiguration.Portions[i];
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
                        GetWikidataLinks(mlpl, code, project, portionOfTitles);
                        curPos += portionSize;
                        break;
                    }
                    catch (WikiApiException e)
                    {
                        Console.WriteLine(e);
                        if (e.Message != "Too large URI")
                            throw;
                        if (portionSize == 1)
                            curPos++;
                    }
                }
            }
        }

        private void GetWikidataLinks(List<MultilingualPage> mlpl, string code, string project, string[] titles)
        {
            var wdLangCode = _wikiCodes.ToProjectCode(code);
            var wikiDataLinks = WikiApiFunctions.GetWikidataLinks(wdLangCode, titles, true);

            foreach (var title in titles)
            {
                foreach (var interwikiItem in mlpl.SelectMany(mlp => mlp.Interwikis).Where(iw => iw.Title == title && iw.Code == code).ToList())
                {
                    interwikiItem.IsWdChecked = true;
                }
            }

            foreach (var wdEntities in wikiDataLinks)
            {
                if (wdEntities.Count == 0)
                    continue;
                var curProjectLinks = _wikiCodes.ToLanguageCodes(wdEntities);
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
                            string.Format("Conflict. Starting from: {0}:{1}. Found items on WikiData: {2} and {3}",
                                          mlp.Interwikis[0].Code,
                                          mlp.Interwikis[0].Title, mlp.WikiDataItem, wdId);
                        continue;
                    }
                    mlp.IsOnWikiData = true;
                    mlp.WikiDataItem = wdId;
                    foreach (var wdIw in curProjectLinks)
                    {
                        if (wdIw.Key == "wikidata")
                            continue;
                        if (mlp.Interwikis.Any(iw => iw.Code == wdIw.Key && iw.Title == wdIw.Value))
                            continue;
                        mlp.Interwikis.Add(new InterwikiItem { Code = wdIw.Key, Title = wdIw.Value });
                    }
                    foreach (var iw in mlp.Interwikis.Where(iw => curProjectLinks.ContainsKey(iw.Code) && curProjectLinks[iw.Code] == iw.Title).ToList())
                    {
                        iw.IsWdChecked = true;
                        iw.IsOnWd = true;
                    }
                }
            }
        }

        private void GetLocalInterwiki(List<MultilingualPage> mlpl, string code, string project)
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
                for (int i = 0; i < _botConfiguration.Portions.Length; i++)
                {
                    int portionSize = _botConfiguration.Portions[i];
                    int bdiff = titlesToCheck.Length - curPos;
                    if (portionSize > bdiff)
                        portionSize = bdiff;
                    var portionOfTitles = titlesToCheck.Skip(curPos).Take(portionSize).ToArray();
                    if (HttpUtility.UrlEncode(string.Join("|", portionOfTitles)).Length > 8000)
                        continue;
                    try
                    {
                        var logstring = string.Format("\t[{0}] Getting local interlanguage links for {1} page(s)",
                            code,
                            portionOfTitles.Length);
                        _commonlog.LogData(logstring, 0);
                        GetLocalInterwiki(mlpl, code, project, portionOfTitles);
                        curPos += portionSize;
                        break;
                    }
                    catch (WikiApiException e)
                    {
                        Console.WriteLine(e);
                        if (e.Message != "Too large URI")
                            throw;
                        if (portionSize == 1)
                            curPos++;
                    }
                }
            }
        }

        private void GetLocalInterwiki(List<MultilingualPage> mlpl, string code, string project, string[] titles)
        {
            var allPagesInterwikis = WikiApiFunctions.GetLocalInterwiki(code, project, titles);

            var newArr = new Dictionary<string, Dictionary<string, string>>();
            foreach (var kvv in allPagesInterwikis)
            {
                var newIw = new Dictionary<string, string>();
                foreach (var kv in kvv.Value)
                {
                    var title = kv.Value;
                    if (title.StartsWith("voy:"))
                        title = title.Substring(4);
                    newIw.Add(kv.Key, title);
                }
                newArr.Add(kvv.Key, newIw);
            }
            allPagesInterwikis = newArr;

            foreach (var mlp in mlpl)
            {
                foreach (var mlpInterwiki in mlp.Interwikis.Where(iw => iw.Code == code && allPagesInterwikis.ContainsKey(iw.Title)).ToList())
                {
                    mlpInterwiki.IsLocalIwChecked = true;
                    var localPageInterwikis = allPagesInterwikis[mlpInterwiki.Title];

                    foreach (var iw in localPageInterwikis.Where(iw => !mlp.Interwikis.Any(mlpIw => mlpIw.Code == iw.Key && mlpIw.Title == iw.Value)).ToList())
                    {
                        if (!_wikiCodes.ContainsLanguageCode(iw.Key))
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
                if (code == "eo")
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
                if (!langGroups.Any())
                    continue;
                var maxLangGroup = langGroups.First();
                if (maxLangGroup.Count() <= 1)
                    continue;
                mlp.HasConflict = true;
                mlp.ConflictDescription =
                    string.Format("Conflict. Starting from: {0}:{1}. Found different local pages: {2}:{3} and {2}:{4}",
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
                                string.Format("Conflict. Namespace of page {0} is not corresponded to wikidata item {1}",
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

            return _botConfiguration.CheckNamespaceConfirmity(lang1, ns1, lang2, ns2);
        }

        private string MakeCreationSummary(Dictionary<string, string> addList, string project)
        {
            return MakeSummary(addList, new Dictionary<string, KeyValuePair<string, string>>(), project, true);
        }

        private string MakeSummary(Dictionary<string, string> addList, Dictionary<string, KeyValuePair<string, string>> updateList, string project, bool isNewItem)
        {
            string summary;
            int addNum = addList.Count;
            int replaceNum = updateList.Count;
            int summaryLengthScore = addNum + replaceNum * 3;
            if (addNum > 0)
                summaryLengthScore++;
            if (replaceNum > 0)
                summaryLengthScore++;
            bool makeLongSummary = summaryLengthScore <= 6;

            if (isNewItem)
            {
                summary = "New item: ";
                foreach (var iw in addList)
                {
                    summary += _wikiCodes.MakeWikiTextLink(iw.Key, iw.Value, makeLongSummary) + ", ";
                }
            }
            else
            {
                string addSummary = "";
                string replaceSummary = "";
                foreach (var iw in addList)
                {
                    addSummary += _wikiCodes.MakeWikiTextLink(iw.Key, iw.Value, makeLongSummary) + ", ";
                }
                foreach (var iw in updateList)
                {
                    if (makeLongSummary)
                    {
                        string oldWikiLink = _wikiCodes.MakeWikiTextLink(iw.Key, iw.Value.Key, true);
                        string newWikiLink = _wikiCodes.MakeWikiTextLink(iw.Key, iw.Value.Value, true);
                        replaceSummary = oldWikiLink + " -> " + newWikiLink + ", ";
                    }
                    else
                        replaceSummary += _wikiCodes.MakeWikiTextLink(iw.Key, "", false) + ", ";
                }

                if (addNum > 0)
                {
                    summary = "Added: " + addSummary.TrimEnd(new[] { ' ', ',' });
                    if (replaceNum > 0)
                        summary += "; updated: " + replaceSummary;
                }
                else
                {
                    summary = "Updated: " + replaceSummary;
                }
            }

            return summary.TrimEnd(new[] { ' ', ',' });
        }

        private string MakeActionLogString(Dictionary<string, string> addList, string project)
        {
            return MakeActionLogString(addList, new Dictionary<string, KeyValuePair<string, string>>(), project, true);
        }

        private string MakeActionLogString(Dictionary<string, string> addList, Dictionary<string, KeyValuePair<string, string>> updateList, string project, bool isNewItem)
        {
            string actionLogString;
            int addNum = addList.Count;
            int replaceNum = updateList.Count;

            if (isNewItem)
            {
                actionLogString = "New item: ";
                foreach (var iw in addList)
                {
                    actionLogString += _wikiCodes.MakeWikiTextLink(iw.Key, iw.Value, true) + ", ";
                }
            }
            else
            {
                string addLogString = "";
                string replaceLogString = "";
                foreach (var iw in addList)
                {
                    addLogString += _wikiCodes.MakeWikiTextLink(iw.Key, iw.Value, true) + ", ";
                }
                foreach (var iw in updateList)
                {
                    string oldWikiLink = _wikiCodes.MakeWikiTextLink(iw.Key, iw.Value.Key, true);
                    string newWikiLink = _wikiCodes.MakeWikiTextLink(iw.Key, iw.Value.Value, true);
                    replaceLogString = oldWikiLink + " -> " + newWikiLink + ", ";
                }

                if (addNum > 0)
                {
                    actionLogString = "Added: " + addLogString.TrimEnd(new[] { ' ', ',' });
                    if (replaceNum > 0)
                        actionLogString += "; updated: " + replaceLogString;
                }
                else
                {
                    actionLogString = "Updated: " + replaceLogString;
                }
            }

            return actionLogString.TrimEnd(new[] { ' ', ',' });
        }

        private Dictionary<string, string> ReorderInterwiki(Dictionary<string, string> src)
        {
            var primaryIws =
                _botConfiguration
                .InterwikiOrder
                .Where(src.ContainsKey)
                .ToDictionary(io => io, io => src[io]);
            var otherIws =
                src.Where(kv => !_botConfiguration.InterwikiOrder.Contains(kv.Key))
                .OrderBy(kv => kv.Key)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            return primaryIws
                .Concat(otherIws)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
    }
}
