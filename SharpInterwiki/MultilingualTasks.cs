using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DotNetWikiBot;
using SharpWikiApiFunctions;

namespace SharpInterwiki
{
    partial class MultilingualPageList
    {/*
        public void MingakabuUpdate()
        {
            _wikiCodes = WikiApiFunctions.GetWikiCodes("meta.wikimedia.org");
            
            var pages = new List<PageInfo>();
            var fromStr = "A";
            var toStr = "Zz";

            LogIn();

            while (true)
            {
                pages.Clear();
                var newFromStr = WikiApiFunctions.FillFromAllPagesApi(pages, "min", fromStr, 500, 0);
           //     WikiApiFunctions.FillAllFromCategoryTreeApi(pages, "min", "Kategori:Asteroid");
                int portionNumber = (pages.Count - 1) / _bigPortionSize + 1;
                for (int i = 0; i < portionNumber; i++)
                {
                    var firstind = i * _bigPortionSize;
                    var num = pages.Count - firstind;
                    if (num > _bigPortionSize)
                        num = _bigPortionSize;
          //          MingakabuUpdate(pages.Skip(firstind).Take(num).Select(p => p.Title).ToList());
                }
            
                if (newFromStr.Length == 0)
                    break;
                if (toStr.Length > 0 && newFromStr.CompareTo(toStr) > 0)
                    break;
                fromStr = newFromStr;
            }
        }
        /*
        public void MingakabuUpdate(List<string> pages)
        {
       //     pages = pages.Where(p => p.StartsWith("(")).ToList();
       //     if(pages.Count == 0)
       //         return;
            var mlpl = new List<MultilingualPage>();
            foreach (var page in pages)
            {
                var ind = page.IndexOf(")");
                if(ind < 0)
                    continue;
                var artitle = "الكويكب 154989".Replace("154989", page.Substring(1, ind - 1));
                var mlp = new MultilingualPage();

                mlp.Interwikis.Add(new InterwikiItem { Title = page, Code = "min" });
                mlp.Interwikis.Add(new InterwikiItem { Title = page, Code = "nl" });
           //     mlp.Interwikis.Add(new InterwikiItem { Title = artitle, Code = "ar" });
                mlpl.Add(mlp);
            }

            var langcode = "min";
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
            if (mlpCreates.Count > 0)
            {
                _commonlog.LogData("Proposed creations:", mlpCreates.Count.ToString(), 1);
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
                try
                {
                    wdItem.createItem(iwList, MakeSummary(iwList, true));
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
                var iwList =
                    ReorderInterwiki(properIws
                                         .ToDictionary(iw => iw.Code, iw => iw.Title));
                Item wdItem = new Item(_wikidataSite, mlpUpdate.WikiDataItem);
                wdItem.Load();
                var codes = _wikiCodes["wiki"];
                var oldIwList = wdItem.links.Where(iw => codes.ContainsKey(iw.Key))
                                      .ToDictionary(iw => codes[iw.Key], iw => iw.Value);
                var diffList = iwList.Where(iw => !oldIwList.ContainsKey(iw.Key))
                                     .ToDictionary(iw => iw.Key, iw => iw.Value);
                try
                {
                    wdItem.setSiteLink(iwList, MakeSummary(diffList, false));
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
         * */
    }
}
