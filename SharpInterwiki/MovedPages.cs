using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using System.Xml;
using DotNetWikiBot;
using SharpWikiApiFunctions;

namespace SharpInterwiki
{
    internal class MovedPageList
    {
        private readonly BotConfiguration _botConfiguration;

        private readonly InterwikiLogger _commonlog;
        private readonly InterwikiLogger _actionlog;
        private readonly InterwikiLogger _conflictlog;

        private const int MaxItNum = 5;
        private int _bigPortionSize = 500;
        private Site _wikidataSite;
        private string _categoryRedirectTemplate = "";
        private string _categoryTalkMovedTemplate = "";

        private LanguageCodes _wikiCodes;

        private readonly Regex _innerLinkRegex = new Regex(@"\[\[([^\]]*)\]\]");

        public MovedPageList(BotConfiguration bc, InputParameters inp)
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

        public void GetCategoryRedirectTemplate(string code, string project)
        {
            if (!_wikiCodes.ContainsLanguageCode(code))
                return;
            var wdCode = _wikiCodes.ToProjectCode(code);
            var wikidataLinks = WikiApiFunctions.GetWikidataLinks("wikidata", new[] { "Q5828850" }, true);
            if (wikidataLinks.Count == 0)
                return;
            if (!wikidataLinks[0].ContainsKey(wdCode))
                return;
            _categoryRedirectTemplate = wikidataLinks[0][wdCode];
            Console.WriteLine("Found category redirect template:\t{0}", _categoryRedirectTemplate);
        }

        public void GetCategoryTalkMovedTemplate(string code, string project)
        {
            if (!_wikiCodes.ContainsLanguageCode(code))
                return;
            var wdCode = _wikiCodes.ToProjectCode(code);
            var wikidataLinks = WikiApiFunctions.GetWikidataLinks("wikidata", new[] { "Q8261497" }, true);
            if (wikidataLinks.Count == 0)
                return;
            if (!wikidataLinks[0].ContainsKey(wdCode))
                return;
            _categoryTalkMovedTemplate = wikidataLinks[0][wdCode];
            Console.WriteLine("Found category talk moved template:\t{0}", _categoryTalkMovedTemplate);
        }

        public void FindMovedCategories(InputParameters inp)
        {
            var newCats = new List<PageInfo>();
            Dictionary<string, object> queryParams = new Dictionary<string, object>
                {
                    {"querytype", "newpages"},
                    {"ns", 14},
                    {"limit", inp.Query},
                    {"withcomments", 1},
                    {"hours", inp.Hours}
                };

            WikiApiFunctions.FillFromApiQuery(newCats, inp.Langcode, inp.Projectcode, queryParams);

            if (newCats.Count == 0)
                return;

            LogIn();
            _wikiCodes = _botConfiguration.GetWikiCodes(inp.Projectcode);
            _botConfiguration.GetNamespaces(inp.Langcode, inp.Projectcode);
            GetCategoryRedirectTemplate(inp.Langcode, inp.Projectcode);

            if (inp.Mode == "all" || inp.Mode == "talkmove")
            {
                ProcessMovedCategoryTalks(inp, newCats);
            }

            if (inp.Mode == "all" || inp.Mode == "catredir")
            {
                GetCategoryRedirectTemplate(inp.Langcode, inp.Projectcode);
                if (_categoryRedirectTemplate.Length != 0)
                {
                    var wikidataLinks =
                        GetWikidataLinks(inp.Langcode, inp.Projectcode, newCats.Select(p => p.Title).ToList());
                    newCats = newCats.Where(p => !wikidataLinks.ContainsKey(p.Title)).ToList();

                    ProcessCategoryRedirects(inp, newCats);
                }
            }

            if (inp.Mode == "all" || inp.Mode == "talkmovedtemplate")
            {
                GetCategoryTalkMovedTemplate(inp.Langcode, inp.Projectcode);
                if (_categoryTalkMovedTemplate.Length != 0)
                {
                    var wikidataLinks =
                        GetWikidataLinks(inp.Langcode, inp.Projectcode, newCats.Select(p => p.Title).ToList());
                    newCats = newCats.Where(p => !wikidataLinks.ContainsKey(p.Title)).ToList();

                    ProcessCategoryTalkMovedTemplates(inp, newCats);
                }
            }

            if (inp.Mode == "all" || inp.Mode == "replacedcategory")
            {
                var wikidataLinks =
                    GetWikidataLinks(inp.Langcode, inp.Projectcode, newCats.Select(p => p.Title).ToList());
                newCats = newCats.Where(p => !wikidataLinks.ContainsKey(p.Title)).ToList();

                ProcessReplacedCategories(inp, newCats);
            }
        }

        public void ProcessReplacedCategories(InputParameters inp, List<PageInfo> categories)
        {
            var listOfBots = new List<PageInfo>();
            var queryParams = new Dictionary<string, object>
                {
                    {"querytype", "users"},
                    {"limit", inp.Query},
                    {"group", "bot"},
                    {"getall", 1}
                };
            WikiApiFunctions.FillFromApiQuery(listOfBots, inp.Langcode, inp.Projectcode, queryParams);
            var categoryBotExclusions = _botConfiguration.GetCategoryBotExclusions(inp.Langcode);
            listOfBots = listOfBots.Where(u => !categoryBotExclusions.Contains(u.Title)).ToList();

            if (categories.Count > 0)
                categories = categories.Where(p => listOfBots.Select(u => u.Title).Contains(p.User)).ToList();

            _commonlog.LogData("Found new categories:", categories.Count.ToString(), 4);
            if (categories.Count <= 0)
            {
                _commonlog.LogData("Finishing", 4);
                return;
            }

            foreach (var cat in categories)
            {
                TryTrackReplacedCategory(inp.Langcode, inp.Projectcode, cat.Title, cat.User, cat.Timestamp);
            }
        }

        private bool TryTrackReplacedCategory(string code, string project, string title, string user, DateTime timestamp)
        {
            try
            {
                TrackReplacedCategory(code, project, title, user, timestamp);
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
            }
            return false;
        }

        private void TrackReplacedCategory(string code, string project, string title, string user, DateTime timestamp)
        {
            _commonlog.LogData("Process new category:", title, 3);

            var pages = new List<PageInfo>();
            Dictionary<string, object> queryParams = new Dictionary<string, object>
                {
                    {"querytype", "category"},
                    {"limit", 500},
                    {"getall", 1},
                    {"categoryname", title}
                };

            WikiApiFunctions.FillFromApiQuery(pages, code, project, queryParams);
            if (pages.Count == 0)
                return;
            if (pages.Count > 50)
                pages = pages.Take(50).ToList();
            string replacedCategory = null;
            foreach (var pi in pages)
            {
                var revisions = WikiApiFunctions.GetRevisions(code, project, pi.Title);
                revisions =
                    revisions
                        .Where(r => r.User == user)
                        .Where(r => r.RedirectTo != "0" && r.Title != "0")
                        .Where(r => Math.Abs(timestamp.Subtract(r.Timestamp).TotalMinutes) < 30)
                        .ToList();
                foreach (var revision in revisions)
                {
                    var diff = WikiApiFunctions.GetRevisionDiff(code, project, revision.RedirectTo, revision.Title);
                    if (diff.Length == 0)
                        continue;
                    var xmlText = "<?xml version=\"1.0\"?>\n<diff>\n" + diff + "\n</diff>";
                    var xmlDiff = new XmlDocument();
                    xmlDiff.LoadXml(xmlText);
                    var trNodes = xmlDiff.SelectNodes("//tr");
                    foreach (XmlNode trNode in trNodes)
                    {
                        string deletedLink = "", addedLink = "";
                        var tdNodes = trNode.SelectNodes("./td");
                        foreach (XmlNode tdNode in tdNodes)
                        {
                            if (tdNode.Attributes == null || tdNode.Attributes["class"] == null)
                                continue;
                            if (tdNode.Attributes["class"].Value == "diff-addedline")
                                addedLink = GetSingleInnerLink(RemoveTags(tdNode.InnerXml));
                            if (tdNode.Attributes["class"].Value == "diff-deletedline")
                                deletedLink = GetSingleInnerLink(RemoveTags(tdNode.InnerXml));
                        }
                        if (string.IsNullOrEmpty(deletedLink) || string.IsNullOrEmpty(addedLink))
                            continue;
                        if (addedLink != title)
                            continue;

                        if (replacedCategory == null)
                            replacedCategory = deletedLink;
                        else if (replacedCategory != deletedLink)
                        {
                            _commonlog.LogData("Found different old categories. Skipping.", 3);
                            return;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(replacedCategory))
            {
                _commonlog.LogData("Old category wasn't found. Skipping.", 3);
                return;
            }

            _commonlog.LogData("Found old category:", replacedCategory, 3);

            if (!CheckOldAndNewCategoryMatching(code, project, replacedCategory, title))
            {
                _commonlog.LogData("Old category exists and doesn't redirect to the checking one. Skipping.", 3);
                return;
            }
            var oldWikidataItem = GetWikidataLinks(code, project, new List<string> { replacedCategory });
            if (!oldWikidataItem.ContainsKey(replacedCategory))
            {
                _commonlog.LogData("Old category hasn't Wikidata item. Skipping.", 3);
                return;
            }

            Console.WriteLine("{0} was replaced by {1}. Wikidata item updating...", replacedCategory, title);
            try
            {
                var wdId = oldWikidataItem[replacedCategory];
                UpdateWikidataLink(code, project, wdId, replacedCategory, title, "rc");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private string RemoveTags(string src)
        {
            int tagStart = 0, tagEnd = 0;
            while (true)
            {
                tagStart = src.IndexOf("<", tagStart);
                if (tagStart < 0)
                    break;
                tagEnd = src.IndexOf(">", tagStart);
                if (tagEnd < 0)
                    break;
                src = src.Remove(tagStart, tagEnd - tagStart + 1);
            }

            return src.Trim();
        }

        private string GetSingleInnerLink(string src)
        {
            var matches = _innerLinkRegex.Matches(src);
            if (matches.Count != 1)
                return "";
            var link = matches[0].Groups[1].Value.Trim();
            var pos = link.IndexOf("|");
            if (pos > 0)
                link = link.Remove(pos).Trim();

            return link;
        }

        private void ProcessMovedCategoryTalks(InputParameters inp, List<PageInfo> categories)
        {
            var movedCatTalks = new List<PageInfo>();
            var movedCategories = new List<PageInfo>();
            _commonlog.LogData("Check new category talks moves", 4);

            Dictionary<string, object> queryParams = new Dictionary<string, object>
                {
                    {"querytype", "log"},
                    {"ns", 14},
                    {"limit", inp.Query},
                    {"type", "move"},
                    {"hours", inp.Hours},
                    {"timefiltration", 1}
                };
            WikiApiFunctions.FillFromApiQuery(movedCatTalks, inp.Langcode, inp.Projectcode, queryParams);
            movedCatTalks = _botConfiguration.FilterByNamespace(movedCatTalks, inp.Langcode, 15);
            _commonlog.LogData("Found moved talks:", movedCatTalks.Count.ToString(), 4);

            foreach (var talkPage in movedCatTalks)
            {
                if (_botConfiguration.GetNamespace(inp.Langcode, talkPage.RedirectTo) != 15)
                    continue;
                var movedCat = new PageInfo
                {
                    Title =
                        _botConfiguration.Namespaces[inp.Langcode][14] +
                        talkPage.Title.Substring(_botConfiguration.Namespaces[inp.Langcode][15].Length),
                    RedirectTo =
                        _botConfiguration.Namespaces[inp.Langcode][14] +
                        talkPage.RedirectTo.Substring(_botConfiguration.Namespaces[inp.Langcode][15].Length)
                };
                movedCategories.Add(movedCat);
            }
            movedCategories =
                movedCategories
                .Where(cat => categories.Select(p => p.Title).Contains(cat.RedirectTo))
                .ToList();

            var catsWithWikidata =
                GetWikidataLinks(inp.Langcode, inp.Projectcode, movedCategories.Select(pi => pi.RedirectTo).ToList());
            movedCategories =
                movedCategories
                .Where(cat => !catsWithWikidata.ContainsKey(cat.RedirectTo))
                .ToList();
            _commonlog.LogData("Categories without Wikidata links:", movedCategories.Count.ToString(), 4);

            var oldCatsWithWikidata =
                GetWikidataLinks(inp.Langcode, inp.Projectcode, movedCategories.Select(pi => pi.Title).ToList());
            movedCategories =
                movedCategories
                .Where(cat => oldCatsWithWikidata.ContainsKey(cat.Title))
                .ToList();
            _commonlog.LogData("Old categories with Wikidata links:", movedCategories.Count.ToString(), 4);

            movedCategories = CheckOldAndNewCategoryMatching(inp.Langcode, inp.Projectcode, movedCategories);

            _commonlog.LogData("Wikidata links to be updated:", movedCategories.Count.ToString(), 4);

            foreach (var cat in movedCategories)
            {
                var wdId = oldCatsWithWikidata[cat.Title];
                UpdateWikidataLink(inp.Langcode, inp.Projectcode, wdId, cat.Title, cat.RedirectTo, "tm");
            }
        }

        private void ProcessCategoryRedirects(InputParameters inp, List<PageInfo> categories)
        {
            foreach (var cat in categories)
            {
                var linkedCats = new List<PageInfo>();
                Dictionary<string, object> queryParams = new Dictionary<string, object>
                {
                    {"querytype", "linkstopage"},
                    {"ns", 14},
                    {"limit", 50},
                    {"title", cat.Title},
                    {"withredirects", 1}
                };
                WikiApiFunctions.FillFromApiQuery(linkedCats, inp.Langcode, inp.Projectcode, queryParams);
                if (linkedCats.Count == 0)
                    continue;
                var oldCatsWithRedirectTemplates =
                    WikiApiFunctions.GetTemplates(inp.Langcode, inp.Projectcode, linkedCats.Select(p => p.Title).ToArray(), 100)
                    .Where(ts => ts.Value.Contains(_categoryRedirectTemplate))
                    .Select(ts => ts.Key)
                    .ToList();
                if (oldCatsWithRedirectTemplates.Count == 0)
                    continue;
                var oldCatsWithWikidata = GetWikidataLinks(inp.Langcode, inp.Projectcode, oldCatsWithRedirectTemplates);
                if (oldCatsWithWikidata.Count != 1)
                    continue;

                var oldTitle = oldCatsWithWikidata.Keys.First();
                var wdId = oldCatsWithWikidata[oldTitle];
                UpdateWikidataLink(inp.Langcode, inp.Projectcode, wdId, oldTitle, cat.Title, "cr");
            }
        }

        public void ProcessCategoryRedirectRange2(InputParameters inp)
        {
            _wikiCodes = _botConfiguration.GetWikiCodes(inp.Projectcode);
            GetCategoryRedirectTemplate(inp.Langcode, inp.Projectcode);

            _commonlog.LogData("", 5);
            _commonlog.LogData("Range processing", 4);
            _commonlog.LogData("Language:", inp.Langcode, 4);
            _commonlog.LogData("Namespace:", inp.Ns.ToString(), 4);

            if (_categoryRedirectTemplate.Length == 0)
            {
                _commonlog.LogData("There is no category redirect template. Finishing", 4);
                return;
            }

            var pages = new List<PageInfo>();
            Dictionary<string, object> queryParams = new Dictionary<string, object>
                {
                    {"querytype", "transclusions"},
                    {"ns", 14},
                    {"limit", inp.Query},
                    {"title", _categoryRedirectTemplate},
                    {"getall", 1}
                };
            WikiApiFunctions.FillFromApiQuery(pages, inp.Langcode, inp.Projectcode, queryParams);

            LogIn();

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
                ProcessCategoryRedirects(inp.Langcode,
                                         inp.Projectcode,
                                         pages.Skip(firstind).Take(num).Select(p => p.Title).ToList());
            }
        }

        public void ProcessCategoryRedirects(string code, string project, List<string> pages)
        {
            _commonlog.LogData("Processing page(s):", pages.Count.ToString(), 3);

            var wikidataLinks = GetWikidataLinks(code, project, pages);
            pages = wikidataLinks.Keys.ToList();
            _commonlog.LogData("Pages with Wikidata links:", pages.Count.ToString(), 3);

            //            var categoryRedirects = GetCategoryRedirects(code, pages);
            //          pages = categoryRedirects;
            //        _commonlog.LogData("Selected category redirects:", pages.Count.ToString(), 3);

            var categoriesWithRedirections = ObtainCategoryRedirections(code, project, pages);
            _commonlog.LogData("Categories with redirections:", categoriesWithRedirections.Count.ToString(), 3);

            var targetCategoryProperties =
                GetProperTitles(code, project, categoriesWithRedirections.Select(pi => pi.RedirectTo).ToList());
            categoriesWithRedirections = categoriesWithRedirections
                .Where(
                    pi =>
                    targetCategoryProperties.Where(cp => !cp.IsMissing)
                                            .Select(cp => cp.Title)
                                            .Contains(pi.RedirectTo))
                .ToList();
            _commonlog.LogData("Existing target categories:", categoriesWithRedirections.Count.ToString(), 3);

            var wikidataLinksOfTargets = GetWikidataLinks(code, project, categoriesWithRedirections.Select(pi => pi.RedirectTo).ToList());
            categoriesWithRedirections =
                categoriesWithRedirections
                    .Where(pi => !wikidataLinksOfTargets.Keys.Contains(pi.RedirectTo))
                    .ToList();
            _commonlog.LogData("Update categories:", categoriesWithRedirections.Count.ToString(), 3);

            foreach (var pi in categoriesWithRedirections)
            {
                var wdId = wikidataLinks[pi.Title];
                UpdateWikidataLink(code, project, wdId, pi.Title, pi.RedirectTo, "crr");
            }
        }

        private void ProcessCategoryTalkMovedTemplates(InputParameters inp, List<PageInfo> categories)
        {
            var catTalkPages =
                categories.Select(
                    cat =>
                    new PageInfo
                        {
                            Title =
                                _botConfiguration.Namespaces[inp.Langcode][15] +
                                cat.Title.Substring(_botConfiguration.Namespaces[inp.Langcode][14].Length),
                            RedirectTo = cat.Title
                        })
                       .ToList();

            var talkTemplates = GetTemplates(inp.Langcode, inp.Projectcode, catTalkPages.Select(pi => pi.Title).ToList());

            var talksWithMoveLabel =
                talkTemplates
                    .Where(pt => pt.Value.Contains(_categoryTalkMovedTemplate))
                    .Select(pt => pt.Key)
                    .ToList();

            catTalkPages = catTalkPages.Where(pi => talksWithMoveLabel.Contains(pi.Title)).ToList();
            var catLinks = GetPageLinks(inp.Langcode, inp.Projectcode, catTalkPages.Select(pi => pi.Title).ToList(), 14);

            var oldDeletedCategories =
                GetProperTitles(inp.Langcode, inp.Projectcode, catLinks.Select(cl => cl.Value[0]).ToList())
                    .Where(pi => pi.IsMissing)
                    .Select(pi => pi.Title)
                    .ToList();
            catLinks = catLinks.Where(cl => oldDeletedCategories.Contains(cl.Value[0]))
                               .ToDictionary(cl => cl.Key, cl => cl.Value);
            var oldWikidataLinks =
                GetWikidataLinks(inp.Langcode, inp.Projectcode, catLinks.Select(cl => cl.Value[0]).ToList());
            catLinks = catLinks.Where(cl => oldWikidataLinks.ContainsKey(cl.Value[0]))
                               .ToDictionary(cl => cl.Key, cl => cl.Value);

            foreach (var cl in catLinks)
            {
                var ctp = catTalkPages.FirstOrDefault(ct => ct.Title == cl.Key);
                if (ctp == null)
                    continue;
                var newTitle = ctp.RedirectTo;
                var oldTitle = cl.Value[0];
                var wdId = oldWikidataLinks[oldTitle];
                UpdateWikidataLink(inp.Langcode, inp.Projectcode, wdId, oldTitle, newTitle, "mtt");
            }
        }

        public void ProcessRedirectRange(InputParameters inp)
        {
            LogIn();
            var fromStr = inp.Fromstr;
            var toStr = inp.Tostr;

            if (string.IsNullOrEmpty(fromStr))
                fromStr = "!";
            if (string.IsNullOrEmpty(toStr))
                toStr = "";

            //    fromStr = HttpUtility.UrlDecode(fromStr).Replace('_', ' ');
            //    toStr = HttpUtility.UrlDecode(toStr).Replace('_', ' ');
            //    fromStr = "B";
            _wikiCodes = _botConfiguration.GetWikiCodes(inp.Projectcode);
            _botConfiguration.GetNamespaces(inp.Langcode, inp.Projectcode);

            _commonlog.LogData("", 5);
            _commonlog.LogData("Range processing", 4);
            _commonlog.LogData("Language:", inp.Langcode, 4);
            _commonlog.LogData("Namespace:", inp.Ns.ToString(), 4);
            _commonlog.LogData("Starting:", fromStr, 4);
            if (toStr.Length > 0)
                _commonlog.LogData("Ending:", toStr, 4);

            var pages = new List<PageInfo>();

            while (true)
            {
                pages.Clear();

                Dictionary<string, object> queryParams = new Dictionary<string, object>
                    {
                        {"querytype", "allpages"},
                        {"ns", inp.Ns},
                        {"limit", inp.Query},
                        {"offset", fromStr},
                        {"redirtype", "redirects"}
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
                    ProcessMovedPortion(inp.Langcode, inp.Projectcode, pages.Skip(firstind).Take(num).ToList());
                }

                if (newFromStr.Length == 0)
                    break;
                if (toStr.Length > 0 && newFromStr.CompareTo(toStr) > 0)
                    break;
                fromStr = newFromStr;
            }
        }

        public void ProcessMovedPages(InputParameters inp)
        {
            _commonlog.LogData("", 5);
            _commonlog.LogData("move page processing", 4);
            _commonlog.LogData("Language:", inp.Langcode, 4);
            _commonlog.LogData("Namespace:", inp.Ns.ToString(), 4);
            _commonlog.LogData("Hours:", inp.Hours.ToString(), 4);
            _commonlog.LogData("Query size:", inp.Query.ToString(), 4);

            var pages = new List<PageInfo>();
            var timeBorder = DateTime.UtcNow.AddMinutes(-_botConfiguration.MoveWaitMin);

            Dictionary<string, object> queryParams = new Dictionary<string, object>
                {
                    {"querytype", "log"},
                    {"ns", 14},
                    {"limit", inp.Query},
                    {"type", "move"},
                    {"hours", inp.Hours},
                    {"timefiltration", 1}
                };
            WikiApiFunctions.FillFromApiQuery(pages, inp.Langcode, inp.Projectcode, queryParams);
            _botConfiguration.GetNamespaces(inp.Langcode, inp.Projectcode);
            pages = pages.Where(pi => pi.Timestamp.CompareTo(timeBorder) <= 0).ToList();
            pages = _botConfiguration.FilterByNamespace(pages, inp.Langcode, -100);
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
                ProcessMovedPortion(inp.Langcode, inp.Projectcode, pages.Skip(firstind).Take(num).ToList());
            }
        }

        public void ProcessMovedPortion(string code, string project, List<PageInfo> pages)
        {
            _commonlog.LogData("First page in portion:", pages[0].Title, 2);

            var wikiDataLinks = GetWikidataLinks(code, project, pages.Select(pi => pi.Title).ToList());
            pages = pages.Where(pi => wikiDataLinks.ContainsKey(pi.Title)).ToList();
            _commonlog.LogData("Old pages with wikidata links:", pages.Count.ToString(), 3);
            if (pages.Count == 0)
                return;

            var pageSequences = pages.Select(pi => new List<PageInfo> { pi }).ToList();

            int it = 0;
            while (true)
            {
                var currentPortion = pageSequences
                    .Where(ps => ps.Count == it + 1)
                    .Select(ps => ps[it].Title)
                    .ToList();
                _commonlog.LogData("Iteration:", it.ToString(), 3);
                _commonlog.LogData("Processing pages:", currentPortion.Count.ToString(), 3);
                if (currentPortion.Count == 0)
                    break;
                var pageProperties = GetProperTitles(code, project, currentPortion);
                foreach (var ps in pageSequences.Where(ps => ps.Count == it + 1))
                {
                    var pp = pageProperties.FirstOrDefault(pi => pi.Title == ps[it].Title);
                    if (pp == null)
                        continue;
                    if (pp.Title == pp.RedirectTo)
                        continue;
                    ps[it] = pp;
                    if (ps.Any(pi => pi.Title == pp.RedirectTo))
                        ps[it].RedirectionSection = "Redirection loop!";  //Detected a redirection loop
                    if (ps[it].NormalizedTitle.Length > 0)
                        ps[it].Title = ps[it].NormalizedTitle;
                    if (ps[it].RedirectTo.Length > 0
                        && ps[it].RedirectionSection.Length == 0)
                        ps.Add(new PageInfo { Title = ps[it].RedirectTo });
                }
                var notRedirectedSequences = pageSequences
                    .Where(ps => ps.Count == it + 1)
                    .Where(ps => ps[it].IsMissing)
                    .Where(ps => !ps.Take(it).Any(pi => pi.Timestamp.Year < 2000))
                    .ToList();
                foreach (var ps in notRedirectedSequences)
                {
                    var title = ps[it].Title;
                    var lastMoveTime = ps.OrderByDescending(pi => pi.Timestamp).Select(pi => pi.Timestamp).First();
                    var titleMoves = new List<PageInfo>();
                    Dictionary<string, object> queryParams = new Dictionary<string, object>
                        {
                            {"querytype", "log"},
                            {"ns", 14},
                            {"limit", 500},
                            {"type", "move"},
                            {"title", title},
                            {"hours", 0},
                            {"timefiltration", 0}
                        };
                    WikiApiFunctions.FillFromApiQuery(titleMoves, code, project, queryParams);

                    if (lastMoveTime.Year >= 2000)
                    {
                        titleMoves =
                            titleMoves.Where(pi => pi.Timestamp.CompareTo(lastMoveTime) > 0)
                                      .OrderBy(pi => pi.Timestamp)
                                      .ToList();
                        if (titleMoves.Count == 0)
                            continue;
                        ps[it] = titleMoves.First();
                        ps.Add(new PageInfo { Title = ps[it].RedirectTo });
                    }
                    else
                    {
                        titleMoves =
                            titleMoves.OrderByDescending(pi => pi.Timestamp)
                                      .ToList();
                        if (titleMoves.Count == 0)
                            continue;
                        ps[it] = titleMoves.First();
                        ps.Add(new PageInfo { Title = ps[it].RedirectTo });
                    }
                }
                it++;
            }

            var processedPages = pageSequences
                .Select(ps =>
                        new PageInfo
                            {
                                Title = ps[0].Title,
                                RedirectTo = ps[ps.Count - 1].Title,
                                RedirectionSection = ps[ps.Count - 1].RedirectionSection
                            })
                .ToList();

            processedPages = processedPages
                .Where(pi => pi.Title != pi.RedirectTo)
                .Where(pi => pi.RedirectionSection.Length == 0)
                .Where(pi => _botConfiguration.CheckNamespaceConfirmity(code, pi.Title, code, pi.RedirectTo))
                .ToList();
            _commonlog.LogData("Pages with found targets:", processedPages.Count.ToString(), 3);

            var existingTargetPages = GetProperTitles(code, project, processedPages.Select(pi => pi.RedirectTo).ToList())
                .Where(pi => !pi.IsMissing)
                .Select(pi => pi.Title)
                .ToList();

            processedPages = processedPages
                .Where(pi => existingTargetPages.Contains(pi.RedirectTo))
                .ToList();
            _commonlog.LogData("Pages with existing targets:", processedPages.Count.ToString(), 3);

            var targetWikiDataLinks = GetWikidataLinks(code, project, processedPages.Select(pi => pi.RedirectTo).ToList());

            processedPages = processedPages
                .Where(pi => !targetWikiDataLinks.ContainsKey(pi.RedirectTo))
                .ToList();
            _commonlog.LogData("Targets without wikidata links:", processedPages.Count.ToString(), 3);

            foreach (var pi in processedPages)
            {
                if (!wikiDataLinks.ContainsKey(pi.Title))
                    continue;
                try
                {
                    var id = wikiDataLinks[pi.Title];
                    UpdateWikidataLink(code, project, id, pi.Title, pi.RedirectTo, "");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        private Dictionary<string, string> GetWikidataLinks(string code, string project, List<string> titles)
        {
            var wikidataTitles = new Dictionary<string, string>();
            if (titles.Count == 0)
                return wikidataTitles;

            if (!_wikiCodes.ContainsLanguageCode(code))
                return wikidataTitles;
            var wdLangCode = _wikiCodes.ToProjectCode(code);

            int curPos = 0;
            while (curPos < titles.Count)
            {
                for (int i = 0; i < _botConfiguration.Portions.Length; i++)
                {
                    int portionSize = _botConfiguration.Portions[i];
                    int bdiff = titles.Count - curPos;
                    if (portionSize > bdiff)
                        portionSize = bdiff;
                    var portionOfTitles = titles.Skip(curPos).Take(portionSize).ToArray();
                    if (HttpUtility.UrlEncode(string.Join("|", portionOfTitles)).Length > 8000)
                        continue;
                    try
                    {
                        var logstring = string.Format("\t[{0}] Getting Wikidata links for {1} page(s)",
                            code,
                            portionOfTitles.Length);
                        _commonlog.LogData(logstring, 0);
                        var wikiDataLinks = WikiApiFunctions.GetWikidataLinks(wdLangCode, portionOfTitles, true);
                        foreach (var wikiDataLink in wikiDataLinks)
                        {
                            if (!wikiDataLink.ContainsKey(wdLangCode) || !wikiDataLink.ContainsKey("wikidata"))
                                continue;
                            string title = wikiDataLink[wdLangCode];
                            string wdTitle = wikiDataLink["wikidata"];
                            if (!wikidataTitles.ContainsKey(title))
                                wikidataTitles.Add(title, wdTitle);
                        }

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

            return wikidataTitles;
        }

        private List<PageInfo> GetProperTitles(string code, string project, List<string> titles)
        {
            int curPos = 0;
            var pages = new List<PageInfo>();
            while (curPos < titles.Count)
            {
                for (int i = 0; i < _botConfiguration.Portions.Length; i++)
                {
                    int portionSize = _botConfiguration.Portions[i];
                    int bdiff = titles.Count - curPos;
                    if (portionSize > bdiff)
                        portionSize = bdiff;
                    var portionOfTitles = titles.Skip(curPos).Take(portionSize).ToArray();
                    if (HttpUtility.UrlEncode(string.Join("|", portionOfTitles)).Length > 8000)
                        continue;
                    try
                    {
                        var logstring = string.Format("\t[{0}] Getting proper titles for {1} page(s)",
                            code,
                            portionOfTitles.Length);
                        _commonlog.LogData(logstring, 0);
                        var pagePortion = WikiApiFunctions.GetProperTitles(code, project, portionOfTitles);
                        pages.AddRange(pagePortion);

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

            return pages;
        }

        private bool CheckOldAndNewCategoryMatching(string code, string project, string oldCategory, string newCategory)
        {
            var oldCategoryProperties = WikiApiFunctions.GetProperTitle(code, project, oldCategory);
            if (oldCategoryProperties.IsMissing)
                return true;

            if (_categoryRedirectTemplate.Length == 0)
                return false;

            var pageTemplates = WikiApiFunctions.GetTemplates(code, project, new[] { oldCategory }, 500);
            if (!pageTemplates.ContainsKey(oldCategory)
                || !pageTemplates[oldCategory].Contains(_categoryRedirectTemplate))
                return false;

            var categoryRedirection = WikiApiFunctions.GetPageLinks(code, project, new[] { oldCategory }, 500, 14);
            if (!categoryRedirection.ContainsKey(oldCategory)
                || categoryRedirection[oldCategory].Count != 1
                || categoryRedirection[oldCategory][0] != newCategory)
                return false;

            return true;
        }

        private List<PageInfo> CheckOldAndNewCategoryMatching(string code, string project, List<PageInfo> categories)
        {
            var oldCategoryProperties = GetProperTitles(code, project, categories.Select(c => c.Title).ToList());
            var matchedCategories =
                categories.Where(
                    c => oldCategoryProperties
                             .Where(oc => oc.IsMissing)
                             .Select(oc => oc.Title)
                             .Contains(c.Title))
                          .ToList();
            if (_categoryRedirectTemplate.Length == 0)
                return matchedCategories;

            var redirectedOldCategories =
                GetCategoryRedirects(code, project, oldCategoryProperties.Where(oc => !oc.IsMissing)
                                                                         .Select(oc => oc.Title)
                                                                         .ToList());
            var categoryRedirections = ObtainCategoryRedirections(code, project, redirectedOldCategories);

            categories =
                categories
                    .Where(c => categoryRedirections.Any(cr => c.Title == cr.Title && c.RedirectTo == cr.RedirectTo))
                    .ToList();

            matchedCategories.AddRange(categories);

            return matchedCategories;
        }

        private List<string> GetCategoryRedirects(string code, string project, List<string> titles)
        {
            var categoryRedirects = new List<string>();
            if (_categoryRedirectTemplate.Length == 0)
                return categoryRedirects;
            int curPos = 0;
            while (curPos < titles.Count)
            {
                for (int i = 0; i < _botConfiguration.Portions.Length; i++)
                {
                    int portionSize = _botConfiguration.Portions[i];
                    int bdiff = titles.Count - curPos;
                    if (portionSize > bdiff)
                        portionSize = bdiff;
                    var portionOfTitles = titles.Skip(curPos).Take(portionSize).ToArray();
                    if (HttpUtility.UrlEncode(string.Join("|", portionOfTitles)).Length > 8000)
                        continue;
                    try
                    {
                        var logstring = string.Format("\t[{0}] Getting templates of {1} page(s)",
                            code,
                            portionOfTitles.Length);
                        _commonlog.LogData(logstring, 0);
                        var pageTemplates = WikiApiFunctions.GetTemplates(code, project, portionOfTitles, 500);

                        foreach (var kv in pageTemplates)
                        {
                            if (kv.Value.Contains(_categoryRedirectTemplate))
                                categoryRedirects.Add(kv.Key);
                        }

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

            return categoryRedirects;
        }

        private List<PageInfo> ObtainCategoryRedirections(string code, string project, List<string> titles)
        {
            var pages = new List<PageInfo>();
            int curPos = 0;
            while (curPos < titles.Count)
            {
                for (int i = 0; i < _botConfiguration.Portions.Length; i++)
                {
                    int portionSize = _botConfiguration.Portions[i];
                    int bdiff = titles.Count - curPos;
                    if (portionSize > bdiff)
                        portionSize = bdiff;
                    var portionOfTitles = titles.Skip(curPos).Take(portionSize).ToArray();
                    if (HttpUtility.UrlEncode(string.Join("|", portionOfTitles)).Length > 8000)
                        continue;
                    try
                    {
                        var logstring = string.Format("\t[{0}] Getting templates of {1} page(s)",
                            code,
                            portionOfTitles.Length);
                        _commonlog.LogData(logstring, 0);
                        var pageLinks = WikiApiFunctions.GetPageLinks(code, project, portionOfTitles, 500, 14);

                        foreach (var kv in pageLinks)
                        {
                            if (kv.Value.Count != 1)
                                continue;
                            var pi = new PageInfo { Title = kv.Key, RedirectTo = kv.Value[0] };
                            pages.Add(pi);
                        }

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

            return pages;
        }

        private Dictionary<string, List<string>> GetTemplates(string code, string project, List<string> titles)
        {
            var allTemplates = new Dictionary<string, List<string>>();
            int curPos = 0;
            while (curPos < titles.Count)
            {
                for (int i = 0; i < _botConfiguration.Portions.Length; i++)
                {
                    int portionSize = _botConfiguration.Portions[i];
                    int bdiff = titles.Count - curPos;
                    if (portionSize > bdiff)
                        portionSize = bdiff;
                    var portionOfTitles = titles.Skip(curPos).Take(portionSize).ToArray();
                    if (HttpUtility.UrlEncode(string.Join("|", portionOfTitles)).Length > 8000)
                        continue;
                    try
                    {
                        var logstring = string.Format("\t[{0}] Getting templates of {1} page(s)",
                            code,
                            portionOfTitles.Length);
                        _commonlog.LogData(logstring, 0);
                        var pageTemplates = WikiApiFunctions.GetTemplates(code, project, portionOfTitles, 500);

                        foreach (var pt in pageTemplates)
                        {
                            if (allTemplates.ContainsKey(pt.Key))
                                continue;
                            allTemplates.Add(pt.Key, pt.Value);
                        }

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

            return allTemplates;
        }

        private Dictionary<string, List<string>> GetPageLinks(string code, string project, List<string> titles, int ns)
        {
            var allPageLinks = new Dictionary<string, List<string>>();
            int curPos = 0;
            while (curPos < titles.Count)
            {
                for (int i = 0; i < _botConfiguration.Portions.Length; i++)
                {
                    int portionSize = _botConfiguration.Portions[i];
                    int bdiff = titles.Count - curPos;
                    if (portionSize > bdiff)
                        portionSize = bdiff;
                    var portionOfTitles = titles.Skip(curPos).Take(portionSize).ToArray();
                    if (HttpUtility.UrlEncode(string.Join("|", portionOfTitles)).Length > 8000)
                        continue;
                    try
                    {
                        var logstring = string.Format("\t[{0}] Getting templates of {1} page(s)",
                            code,
                            portionOfTitles.Length);
                        _commonlog.LogData(logstring, 0);
                        var pageLinks = WikiApiFunctions.GetPageLinks(code, project, portionOfTitles, 500, ns);

                        foreach (var pl in pageLinks)
                        {
                            if (allPageLinks.ContainsKey(pl.Key))
                                continue;
                            allPageLinks.Add(pl.Key, pl.Value);
                        }

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

            return allPageLinks;
        }

        private void UpdateWikidataLink(string code, string project, string wdTitle, string oldTitle, string newTitle,
                                        string mark)
        {
            var wdItem = new Item(_wikidataSite, wdTitle);
            wdItem.Load();
            if (mark.Length > 0)
                mark = string.Format("({0}) ", mark);
            var projectIwCode = _wikiCodes.ToProjectCode(code);

            var comment = mark + MakeSummary(code, project, oldTitle, newTitle);

            wdItem.setSiteLink(projectIwCode, newTitle, comment);
            var logstring =
                string.Format("Updated interwiki om item Q{0}: {1} ",
                wdItem.id,
                comment);
            _actionlog.LogData(logstring, 3);
        }

        private string MakeSummary(string code, string project, string oldTitle, string newTitle)
        {
            string summary = "Updated: ";

            string oldWikiLink = _wikiCodes.MakeWikiTextLink(code, oldTitle, true);
            string newWikiLink = _wikiCodes.MakeWikiTextLink(code, newTitle, true);
            summary += oldWikiLink + " -> " + newWikiLink + ", ";

            return summary.TrimEnd(new[] { ' ', ',' });
        }
    }
}
