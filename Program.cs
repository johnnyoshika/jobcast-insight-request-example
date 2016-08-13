using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JobcastInsightRequest
{
    class Program
    {
        static void Main(string[] args)
        {
            
            using (var entities = new bcjobs_analyticsEntities())
            {
                var jobs = (from j in entities.Jobs
                                        .Include(j => j.JobCategories)
                            where j.Processor.Name == "BotcodeDedupe" && j.CreatedAt >= new DateTime(2016, 4, 1) && j.CreatedAt < new DateTime(2016, 7, 1)
                            orderby j.Id
                            select j)
                            .AsEnumerable()
                            .Select(j => new JobInfo
                            {
                                ReferenceId = j.ReferenceId,
                                Source = j.JobSource.Name,
                                Title = Regex.Replace(j.JobTitle.Text, @"\r\n?|\n", " "),
                                Description = Regex.Replace(j.Description, @"\r\n?|\n", " "),
                                Parsed = entities.Jobs.Any(i => i.Processor.Name == "BotcodeDedupe.Sovren" && i.ReferenceId == j.ReferenceId && i.Source_Id == j.Source_Id),
                                Categorized = j.JobCategories.Any()
                            }).ToArray();

                var groups = jobs
                    .Select((j, i) => new { Index = i, Job = j })
                    .ToLookup(x => x.Index / 1000);

                foreach (var group in groups)
                {
                    var client = new HttpClient();
                    var output = client
                        .PostAsync(
                            "http://insight.jobcast.net/parsing/jobs/technology", 
                            new StringContent(string.Join(Environment.NewLine, group.Select(g => g.Job.Title + " " + g.Job.Description)))
                        )
                        .Result
                        .Content
                        .ReadAsStringAsync()
                        .Result;

                    var scores = JArray.Parse(output);
                    for (int i = 0; i < scores.Count; i++)
                    {
                        dynamic score = scores[i];
                        jobs[group.ElementAt(i).Index].Technology = score.job_category[0].text == "technology";
                    }
                }

                var sb = new StringBuilder("Differs,Parsed,Categorized,Technology,ReferenceId,Source,Title");
                foreach (var job in jobs)
                    sb.AppendLine($"{(job.Parsed == job.Technology ? 0 : 1)},{job.Parsed},{job.Categorized},{job.Technology},{job.ReferenceId.WrapQuotes()},{job.Source.WrapQuotes()},{job.Title.WrapQuotes()}");

                File.WriteAllText($"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}\\jobinfo.csv", sb.ToString());

            }
        }
    }

    class JobInfo
    {
        public string ReferenceId { get; set; }
        public string Source { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool Parsed { get; set; }
        public bool Categorized { get; set; }
        public bool Technology { get; set; }
    }

    public static class StringExtensions
    {
        public static string WrapQuotes(this string s)
        {
            return "\"" + s + "\"";
        }
    }
}
