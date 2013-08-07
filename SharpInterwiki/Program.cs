using System;
using System.Globalization;
using DotNetWikiBot;

namespace SharpInterwiki
{
    class Program
    {
        static void Main(string[] args)
        {
            var botconfig = "";
            var catname = "";
            var langcode = "";
            var namespacestr = "";
            var querystr = "";
            var typestr = "";
            var fromstr = "";
            var tostr = "";
            var hoursstr = "";
            var daysstr = "";
            var fullcheck = false;
            foreach (var arg in args)
            {
                if (arg.StartsWith("-configuration:"))
                    botconfig = arg.Substring(15).Trim();
                if (arg.StartsWith("-fromcat:"))
                    catname = arg.Substring(9).Trim();
                if (arg.StartsWith("-lang:")) 
                    langcode = arg.Substring(6).Trim();
                if (arg.StartsWith("-ns:"))
                    namespacestr = arg.Substring(4).Trim();
                if (arg.StartsWith("-namespace:"))
                    namespacestr = arg.Substring(11).Trim();
                if (arg.StartsWith("-query:"))
                    querystr = arg.Substring(7).Trim();
                if (arg.StartsWith("-type:"))
                    typestr = arg.Substring(6).Trim();
                if (arg.StartsWith("-from:"))
                    fromstr = arg.Substring(6).Trim();
                if (arg.StartsWith("-to:"))
                    tostr = arg.Substring(4).Trim();
                if (arg.StartsWith("-days:"))
                    daysstr = arg.Substring(6).Trim();
                if (arg.StartsWith("-hours:"))
                    hoursstr = arg.Substring(7).Trim();
                if (arg.StartsWith("-fullcheck"))
                    fullcheck = true;
            }
           
            int ns;
            int querysize;
            int days, hours;
            if (!int.TryParse(namespacestr, out ns))
                ns = 0;
            if (!int.TryParse(querystr, out querysize))
                querysize = 100;
            if (!int.TryParse(daysstr, out days))
                days = 0;
            if (!int.TryParse(hoursstr, out hours))
                hours = 0;
            hours += days*24;
            if (hours <= 0)
                hours = -1;

            Console.WriteLine("Language: " + langcode);
            Console.WriteLine("Category: " + catname);
            Console.WriteLine("Namespace: " + ns);
            Console.WriteLine("Query: " + querystr);

            var botConfigugation = new BotConfiguration();
            var res = botConfigugation.ReadConfiguration(botconfig);
            botConfigugation.ReadNamespaceConformity();
            if (!res)
            {
                Console.WriteLine("Incorrect configuration file");
                return;
            }

            try
            {
                var mlpl = new MultilingualPageList(botConfigugation);

                if (typestr == "new")
                    mlpl.ProcessNewPages(langcode, ns, hours, querysize, fullcheck);
                if (typestr == "range")
                    mlpl.ProcessRangePages(langcode, ns, fromstr, tostr, querysize, fullcheck);
                if (typestr == "page")
                    mlpl.ProcessPage(langcode, ns, fromstr, fullcheck);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

        }       
    }
}
