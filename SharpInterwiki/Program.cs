using System;
using System.Collections.Generic;
using System.Globalization;
using DotNetWikiBot;

namespace SharpInterwiki
{
    class Program
    {
        static void Main(string[] args)
        {
            InputParameters inputParameters = new InputParameters();
            var res = inputParameters.ParseParameters(args);
            if (!res)
                return;

            var botConfiguration = new BotConfiguration();
            res = botConfiguration.ReadConfiguration(inputParameters.Botconfig);
            botConfiguration.ReadNamespaceConformity();
            if (!res)
            {
                Console.WriteLine("Incorrect configuration file");
                return;
            }

            try
            {
                var mlpl = new MultilingualPageList(botConfiguration, inputParameters);
                var mopl = new MovedPageList(botConfiguration, inputParameters);

                if (inputParameters.Type == "new")
                    mlpl.ProcessNewPages(inputParameters);
                if (inputParameters.Type == "range")
                    mlpl.ProcessRangePages(inputParameters);
                if (inputParameters.Type == "page")
                    mlpl.ProcessPage(inputParameters);
                if (inputParameters.Type == "cat" || inputParameters.Type == "category")
                    mlpl.ProcessCategoryPages(inputParameters);
                if (inputParameters.Type == "user" || inputParameters.Type == "usercontribs")
                    mlpl.ProcessUserContributions(inputParameters);
                if (inputParameters.Type == "movecat")
                    mopl.FindMovedCategories(inputParameters);
                if (inputParameters.Type == "movecatrange")
                    mopl.ProcessCategoryRedirectRange2(inputParameters);
                if (inputParameters.Type == "moverange")
                    mopl.ProcessRedirectRange(inputParameters);
                if (inputParameters.Type == "move")
                    mopl.ProcessMovedPages(inputParameters);

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
}
