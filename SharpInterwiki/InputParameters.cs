using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpInterwiki
{
    public class InputParameters
    {
        public string Botconfig { get; private set; }
        public string Catname { get; private set; }
        public string Langcode { get; private set; }
        public string Projectcode { get; private set; }
        public int Ns { get; private set; }
        public int Query { get; private set; }
        public string Type { get; private set; }
        public string Mode { get; private set; }
        public string Fromstr { get; private set; }
        public string Tostr { get; private set; }
        public int Hours { get; private set; }
        public bool Fullcheck { get; private set; }
        public bool OnlyUpdate { get; private set; }
        public string ProcessId { get; private set; }
        public int Depth { get; private set; }
        public string User { get; private set; }

        public bool ParseParameters(string[] args)
        {
            Botconfig = "";
            Type = "";
            Mode = "";
            Catname = "";
            Langcode = "";
            Projectcode = "";
            Fromstr = "";
            Tostr = "";
            Fullcheck = false;
            OnlyUpdate = false;
            ProcessId = "";
            var namespacestr = "";
            var querystr = "";
            var hoursstr = "";
            var daysstr = "";
            var depthstr = "";

            foreach (var arg in args)
            {
                if (arg.StartsWith("-configuration:"))
                    Botconfig = arg.Substring(15).Trim();
                if (arg.StartsWith("-fromcat:"))
                    Catname = arg.Substring(9).Trim();
                if (arg.StartsWith("-lang:"))
                    Langcode = arg.Substring(6).Trim();
                if (arg.StartsWith("-project:"))
                    Projectcode = arg.Substring(9).Trim();
                if (arg.StartsWith("-ns:"))
                    namespacestr = arg.Substring(4).Trim();
                if (arg.StartsWith("-namespace:"))
                    namespacestr = arg.Substring(11).Trim();
                if (arg.StartsWith("-query:"))
                    querystr = arg.Substring(7).Trim();
                if (arg.StartsWith("-type:"))
                    Type = arg.Substring(6).Trim();
                if (arg.StartsWith("-mode:"))
                    Mode = arg.Substring(6).Trim();
                if (arg.StartsWith("-from:"))
                    Fromstr = arg.Substring(6).Trim();
                if (arg.StartsWith("-to:"))
                    Tostr = arg.Substring(4).Trim();
                if (arg.StartsWith("-days:"))
                    daysstr = arg.Substring(6).Trim();
                if (arg.StartsWith("-hours:"))
                    hoursstr = arg.Substring(7).Trim();
                if (arg.StartsWith("-depth:"))
                    depthstr = arg.Substring(7).Trim();
                if (arg.StartsWith("-user:"))
                    User = arg.Substring(6).Trim();
                if (arg.StartsWith("-fullcheck"))
                    Fullcheck = true;
                if (arg.StartsWith("-onlyupdate"))
                    OnlyUpdate = true;
                if (arg.StartsWith("-id:"))
                    ProcessId = arg.Substring(4).Trim();
            }

            int result;
            int days, hours;
            if (!int.TryParse(namespacestr, out result))
                result = 0;
            Ns = result;
            if (!int.TryParse(querystr, out result))
                result = 100;
            Query = result;
            if (!int.TryParse(depthstr, out result))
                result = 0;
            Depth = result;
            if (!int.TryParse(daysstr, out days))
                days = 0;
            if (!int.TryParse(hoursstr, out hours))
                hours = 0;
            Hours = hours + days*24;
            if (Hours <= 0)
                Hours = -1;
            

            if (Type.Length == 0)
            {
                Console.WriteLine("Insufficient type. Finishing.");
                return false;
            }

            if (!CheckLangAndProjectCodes())
            {
                Console.WriteLine("Insufficient language code. Finishing.");
                return false;
            }

            if (Type == "new" && Hours < 0)
            {
                Console.WriteLine("Time range is not set.");
                return false;
            }

            if ((Type == "cat" || Type == "category") && Catname.Length == 0)
            {
                Console.WriteLine("Category name is not set.");
                return false;
            }

            if ((Type == "user" || Type == "usercontribs") && User.Length == 0)
            {
                Console.WriteLine("User name is not set.");
                return false;
            }

            if (string.IsNullOrEmpty(Mode))
                Mode = "all";

            Console.WriteLine("Language: " + Langcode);
            Console.WriteLine("Project: " + Projectcode);
            Console.WriteLine("Category: " + Catname);
            Console.WriteLine("Namespace: " + Ns);
            Console.WriteLine("Query: " + querystr);

            if (Projectcode != "wikipedia" && Projectcode != "wikivoyage" && Projectcode != "wikisource")
            {
                Console.WriteLine("Unsupported project. Finishing");
                return false;
            }

            return true;
        }

        private bool CheckLangAndProjectCodes()
        {
            if (Langcode.Length == 0)
                return false;
            var pCode = "";
            CheckProject("wiki", ref pCode);
            CheckProject("wiktionary", ref pCode);
            CheckProject("wikibooks", ref pCode);
            CheckProject("wikinews", ref pCode);
            CheckProject("wikiquote", ref pCode);
            CheckProject("wikisource", ref pCode);
            CheckProject("wikiversity", ref pCode);
            CheckProject("wikivoyage", ref pCode);
            if (Projectcode.Length == 0 && pCode.Length > 0)
                Projectcode = pCode;
            if (Projectcode == "wiki" || Projectcode.Length == 0)
                Projectcode = "wikipedia";

            return true;
        }

        private void CheckProject(string baseProject, ref string pCode)
        {
            if (!Langcode.EndsWith(baseProject))
                return;
            Langcode = Langcode.Remove(Langcode.Length - baseProject.Length);
            pCode = baseProject;
        }
    }
}
