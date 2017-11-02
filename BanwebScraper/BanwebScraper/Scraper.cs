﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using HtmlAgilityPack;
using MySql.Data.MySqlClient;

namespace BanwebScraper
{
    internal sealed class Scraper : IDisposable
    {
        #region Variables
        private const string
            RefreshUrl      =  "https://banwebplusplus.me/public/updateCourseInfo.php",
            CourseInfoUrl   =  "https://www.banweb.mtu.edu/pls/owa/stu_ctg_utils.p_online_all_courses_ug",
            SectionInfoUrl  =  "https://banwebplusplus.me/banwebFiles/";

        private static CancellationTokenSource         _cts;
        private static CancellationToken               _ct;
        private static AutoResetEvent                  _waitForInputFlag,
                                                       _gotInputFlag;

        private readonly HtmlWeb                       _web;
        private readonly string                        _connectionString;
        private readonly string[]                      _coursePushCommands,
                                                       _sectionPushCommands;
        private readonly Dictionary<string, DateTime>  _lastPushInfo;

        private bool                                   _firstRun = true;
        private List<List<object>>                     _courseParameters,
                                                       _sectionParameters;
        private HashSet<string>                        _sectionList,
                                                       _courseList;
        #endregion

        #region Initialization
        public Scraper()
        {
            _connectionString = new MySqlConnectionStringBuilder
            {
                Server = "159.203.102.52",
                Port = 3306,
                Database = "banwebpp",
                UserID = "dbuser",
                Password = "BanWeb++"
            }.ConnectionString;
            _web = new HtmlWeb();

            var waiterThread = new Thread(Waiter) {IsBackground = true};
            _waitForInputFlag = new AutoResetEvent(false);
            _gotInputFlag = new AutoResetEvent(false);
            _cts = new CancellationTokenSource();
            _ct = _cts.Token;
            waiterThread.Start();

            _lastPushInfo = new Dictionary<string, DateTime>();
            _sectionPushCommands = new[]
            {
                "INSERT INTO Sections (CourseNum,CRN,SectionNum,Days,SectionTime,Location,SectionActual,Capacity,SlotsRemaining,Instructor,Dates,Fee,Year,Semester) VALUES (CONCAT(@Subj,' ',@Crse),@CRN,@Sec,@Days,@Time,@Loc,@Act,@Cap,@Rem,@Inst,@Dates,@Fee,@Yr,@Sem)",
                "UPDATE Sections SET CourseNum=CONCAT(@Subj,' ',@Crse), SectionNum=@Sec, Days=@Days, SectionTime=@Time, Location=@Loc, SectionActual=@Act, Capacity=@Cap, SlotsRemaining=@Rem, Instructor=@Inst, Dates=@Dates, Fee=@Fee WHERE CRN=@CRN AND Year=@Yr AND Semester=@Sem"
            };
            _sectionParameters = new List<List<object>>
            {
                new List<object> {"@CRN", MySqlDbType.Int32},
                new List<object> {"@Subj", MySqlDbType.VarChar, 6},
                new List<object> {"@Crse", MySqlDbType.VarChar, 4},
                new List<object> {"@Sec", MySqlDbType.VarChar, 3},
                new List<object> {"@Cmp", MySqlDbType.VarChar, 3},
                new List<object> {"@Cred", MySqlDbType.VarChar, 16},
                new List<object> {"@Title", MySqlDbType.VarChar, 255},
                new List<object> {"@Days", MySqlDbType.VarChar, 10},
                new List<object> {"@Time", MySqlDbType.VarChar, 64},
                new List<object> {"@Cap", MySqlDbType.Int32},
                new List<object> {"@Act", MySqlDbType.Int32},
                new List<object> {"@Rem", MySqlDbType.Int32},
                new List<object> {"@Inst", MySqlDbType.VarChar, 128},
                new List<object> {"@Dates", MySqlDbType.VarChar, 64},
                new List<object> {"@Loc", MySqlDbType.VarChar, 8},
                new List<object> {"@Fee", MySqlDbType.VarChar, 255},
                new List<object> {"@Yr", MySqlDbType.Int32},
                new List<object> {"@Sem", MySqlDbType.VarChar, 16}
            };
            
            _coursePushCommands = new[]
            {
                "INSERT INTO Courses (CourseNum,CourseName,Description,SemestersOffered,Credits,LectureCredits,RecitationCredits,LabCredits,Restrictions,Prereq,Coreq) VALUES(@Crse,@Title,@Descr,@Sem,@Cred,@Lec,@Rec,@Lab,@Rest,@Prereqs,@Coreqs)",
                "UPDATE Courses SET CourseName=@Title, Description=@Descr, SemestersOffered=@Sem, Credits=@Cred, LectureCredits=@Lec, RecitationCredits=@Rec, LabCredits=@Lab, Restrictions=@Rest, Prereq=@Prereqs, Coreq=@Coreqs WHERE CourseNum=@Crse"
            };
            _courseParameters = new List<List<object>>
            {
                new List<object> {"@Crse", MySqlDbType.VarChar, 8},
                new List<object> {"@Title", MySqlDbType.VarChar, 255},
                new List<object> {"@Descr", MySqlDbType.VarChar, 5000},
                new List<object> {"@Cred", MySqlDbType.VarChar, 64},
                new List<object> {"@Lec", MySqlDbType.Int32},
                new List<object> {"@Rec", MySqlDbType.Int32},
                new List<object> {"@Lab", MySqlDbType.Int32},
                new List<object> {"@Sem", MySqlDbType.VarChar, 32},
                new List<object> {"@Rest", MySqlDbType.VarChar, 255},
                new List<object> {"@Prereqs", MySqlDbType.VarChar, 255},
                new List<object> {"@Coreqs", MySqlDbType.VarChar, 255}
            };
        }
        public void Run()
        {
            // modified & implemented wait timer: https://stackoverflow.com/a/18342182
            var sw = new Stopwatch();
            while (true)
            {
                sw.Start();
                for (var i = 0; !PushCourseInfo() && i < 5; i++) WaitForInput(10000);
                for (var i = 0; i < 24; i++)
                {
                    PushAllSectionInfo();
                    Console.Write($"[{DateTime.Now:s}]  -  Waiting for next run, press <Enter> to quit ");
                    sw.Stop();
                    if (WaitForInput(3600000 - (int) sw.ElapsedMilliseconds)) return;
                    Console.WriteLine();
                    sw.Restart();
                }
            }
        }
        #endregion

        #region Controller Methods
        private void PushAllSectionInfo()
        {
            Console.Write($"[{DateTime.Now:s}]  -  Section Info Running");
            _web.Load(RefreshUrl);

            _sectionList = RunQuery("SELECT crn FROM Sections");
            foreach (var section in GetSections())
            {
                Console.Write(".");
                if (_lastPushInfo.ContainsKey(section.Key) && section.Value.Equals(_lastPushInfo[section.Key])) continue;
                for (var i = 0; !PushSectionInfo(section.Key) && i < 5; i++) WaitForInput(10000);
                if (!_lastPushInfo.ContainsKey(section.Key)) _lastPushInfo.Add(section.Key, section.Value);
                else _lastPushInfo[section.Key] = section.Value;
            }
            _firstRun = false;

            Console.WriteLine(" Done");
        }
        private bool PushSectionInfo(string pageName)
        {
            var doc = new HtmlDocument();
            var year = pageName.Substring(0, 4);
            string semester;
            switch (pageName.Substring(4, 2))
            {
                case "01": semester = "Spring"; break;
                case "05": semester = "Summer"; break;
                case "08": semester = "Fall"; break;
                default: semester = "Unknown"; break;
            }

            try
            {
                doc.Load(new WebClient().OpenRead(SectionInfoUrl + pageName));
            }
            catch (Exception e)
            {
                Console.WriteLine($"\n{e.GetType()} encountered while getting section html.\n{e.StackTrace}");
                GC.Collect();
                return false;
            }

            try
            {
                Push(ParseSections(doc), _sectionPushCommands, _sectionParameters, _sectionList, year, semester);
            }
            catch (Exception e)
            {
                Console.WriteLine($"\n{e.GetType()} encountered while parsing section info.\n{e.StackTrace}");
                GC.Collect();
                return false;
            }

            GC.Collect();
            return true;
        }

        private bool PushCourseInfo()
        {
            Console.Write($"[{DateTime.Now:s}]  -  Course Info Running....... ");
            _courseList = RunQuery("SELECT CourseNum FROM Courses");
            HtmlDocument doc;

            try
            {
                doc = _web.Load(CourseInfoUrl);
            }
            catch (Exception e)
            {
                Console.WriteLine($"\n{e.GetType()} encountered while getting course html.\n{e.StackTrace}");
                GC.Collect();
                return false;
            }

            try
            {
                Push(ParseCourses(doc), _coursePushCommands, _courseParameters, _courseList);
            }
            catch (Exception e)
            {
                Console.WriteLine($"\n{e.GetType()} encountered while parsing course info.\n{e.StackTrace}");
                GC.Collect();
                return false;
            }

            GC.Collect();
            Console.WriteLine("Done");
            return true;
        }
        #endregion

        #region Parsing
        private IEnumerable<KeyValuePair<string, DateTime>> GetSections()
        {
            var sections = new List<KeyValuePair<string, DateTime>>();
            var rows = _web.Load(SectionInfoUrl).DocumentNode.SelectSingleNode("//table").SelectNodes("tr");
            for (var i = 3; i < rows.Count - 2; i++)
            {
                var cells = rows[i].SelectNodes("td");
                if (_firstRun || int.Parse(cells[1].InnerText.Substring(0, 4)) > DateTime.Today.Year || int.Parse(cells[1].InnerText.Substring(0, 4)) == DateTime.Today.Year && int.Parse(cells[1].InnerText.Substring(4, 2)) >= DateTime.Today.Month)
                    sections.Add(new KeyValuePair<string, DateTime>(cells[1].InnerText, DateTime.Parse(cells[2].InnerText)));
            }
            return sections;
        }
        private static List<List<string>> ParseSections(HtmlDocument doc)
        {
            var resultSet = new List<List<string>>();
            var table = doc.DocumentNode.SelectNodes("//table")[3];
            foreach (var row in table.SelectNodes("tr"))
            {
                try
                {
                    if (row.SelectNodes("td") == null) continue;
                    var resultRow = new List<string>();

                    var cells = row.SelectNodes("td");
                    if (cells[0].InnerText == "&nbsp;")
                    {
                        for (var i = 0; i < cells.Count; i++)
                            resultSet[resultSet.Count - 1][i] += resultSet[resultSet.Count - 1][i] != cells[i].InnerText && cells[i].InnerText != "&nbsp;" ? "|" + cells[i].InnerText : "";
                        continue;
                    }

                    foreach (var cell in row.SelectNodes("td"))
                    {
                        resultRow.Add(cell.InnerText);
                        for (var i = 1; i < int.Parse(cell.Attributes["colspan"]?.Value ?? "0"); i++)
                            resultRow.Add(cell.InnerText);
                    }
                    if (resultRow.Count > 0) resultSet.Add(resultRow);
                }
                catch (Exception e)
                {
                    Console.WriteLine("\n" + e);
                }
            }
            return resultSet;
        }

        private static List<List<string>> ParseCourses(HtmlDocument doc)
        {
            var resultString = "";
            var resultRow = new List<string>();
            var resultSet = new List<List<string>>();

            var nodes = doc.GetElementbyId("content").ChildNodes;
            for (var i = 0; i < nodes.Count; i++)
            {
                try
                {
                    if (i <= 10) continue;
                    switch (nodes[i].Name)
                    {
                        case "br" when nodes[i - 1].Name == "br":
                            resultRow.RemoveAt(resultRow.Count - 1);
                            resultSet.Add(resultRow);
                            resultRow = new List<string>();
                            break;
                        case "br":
                            resultRow.Add(resultString.Trim('\n', ' '));
                            resultString = string.Empty;
                            break;
                        case "A":
                        case "hr":
                        case "h3":
                            break;
                        default:
                            resultString += nodes[i].InnerText;
                            break;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("\n" + e);
                }
            }

            NormalizeCourses(resultSet);
            return resultSet;
        }
        private static void NormalizeCourses(IEnumerable<List<string>> courses)
        {
            foreach (var course in courses)
            {
                try
                {
                    var sa = course[0].Split('-');
                    course[0] = sa[0].Trim('\n', ' ');
                    course.Insert(1, string.Join("", sa.Skip(1)).Trim('\n', ' '));
                    course[3] = course[3].Substring(9).Trim('\n', ' ');

                    for (var i = 4; i <= 8; i++)
                        if (i < course.Count) HandleOptional(course, i);
                        else course.Insert(i, null);

                    sa = course[4]?.Split('-') ?? new string[] { null, null, null };
                    course[4] = sa[0];
                    course.Insert(5, sa[1]);
                    course.Insert(6, sa[2]);

                    if (course[9] != null) course[9] = course[9].Replace(" or ", "|").Replace(" and ", "&");
                    if (course[10] != null) course[10] = course[10].Replace(" or ", "|").Replace(" and ", "&");
                }
                catch (Exception e)
                {
                    Console.WriteLine("\n" + e);
                }
            }
        }
        private static void HandleOptional(IList<string> l, int i)
        {
            string s;
            if (l[i].StartsWith("Lec-Rec-Lab:") && i >= 4)
                s = l[i].Substring(12, l[i].Length - 13).Trim(' ', '(', ')', '\n');
            else if (l[i].StartsWith("Semesters Offered:") && i >= 5)
                s = l[i].Substring(18).Trim(' ', '\n');
            else if (l[i].StartsWith("Restrictions:") && i >= 6)
                s = l[i].Substring(13).Trim(' ', '\n');
            else if (l[i].StartsWith("Pre-Requisite(s):") && i >= 7)
                s = l[i].Substring(17).Trim(' ', '\n');
            else if (l[i].StartsWith("Co-Requisite(s):") && i >= 7)
                s = l[i].Substring(16).Trim(' ', '\n');
            else s = string.Empty;

            if (s == string.Empty) l.Insert(i, null);
            else l[i] = s;
        }
        #endregion

        #region Database Push
        private void Push(ICollection<List<string>> resultSet, IReadOnlyList<string> commands, IEnumerable<List<object>> parameterSet, ICollection<string> set, string year = "", string semester = "")
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                using (var insertCommand = new MySqlCommand(commands[0], connection, transaction))
                using (var updateCommand = new MySqlCommand(commands[1], connection, transaction))
                {
                    foreach (var parameter in parameterSet)
                        if (parameter.Count == 2)
                        {
                            insertCommand.Parameters.Add((string)parameter[0], (MySqlDbType)parameter[1]);
                            updateCommand.Parameters.Add((string)parameter[0], (MySqlDbType)parameter[1]);
                        }
                        else
                        {
                            insertCommand.Parameters.Add((string)parameter[0], (MySqlDbType)parameter[1], (int)parameter[2]);
                            updateCommand.Parameters.Add((string)parameter[0], (MySqlDbType)parameter[1], (int)parameter[2]);
                        }
                    insertCommand.Prepare();
                    updateCommand.Prepare();

                    foreach (var row in resultSet)
                    {
                        try
                        {
                            var command = set.Contains(ScrubHtml(row[0])) ? updateCommand : insertCommand;
                            for (var i = 0; i < command.Parameters.Count; i++)
                            {
                                try
                                {
                                    switch (command.Parameters[i].MySqlDbType)
                                    {
                                        case MySqlDbType.Int32:
                                            command.Parameters[i].Value = ScrubHtmlInt(row[i]);
                                            break;
                                        case MySqlDbType.VarChar:
                                            command.Parameters[i].Value = ScrubHtml(row[i]);
                                            break;
                                    }
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                    switch (command.Parameters[i].ParameterName)
                                    {
                                        case "@Yr": command.Parameters[i].Value = year; break;
                                        case "@Sem": command.Parameters[i].Value = semester; break;
                                        default: Console.WriteLine("Wut"); break;
                                    }
                                }
                            }
                            command.ExecuteNonQuery();
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("\n" + e);
                        }
                    }
                    transaction.Commit();

                    foreach (var result in resultSet) result.Clear();
                    resultSet.Clear();
                }
            }
        }

        private static string ScrubHtml(string s)
        {
            if (s == null) return null;
            s = Regex.Replace(s.Replace("&nbsp;", "").Trim('\n', ' '), @"\s{2,}", " ");
            return s == string.Empty ? null : s;
        }
        private static int? ScrubHtmlInt(string htmlstring)
        {
            var s = ScrubHtml(htmlstring);
            return s == null ? default(int?) : int.Parse(s);
        }

        private HashSet<string> RunQuery(string query)
        {
            var dt = new DataTable();
            var result = new HashSet<string>();

            try
            {
                using (var connection = new MySqlConnection(_connectionString))
                using (var command = new MySqlCommand(query, connection))
                {
                    connection.Open();
                    dt.Load(command.ExecuteReader());
                    foreach (DataRow dr in dt.Rows)
                        result.Add(dr[0].ToString());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("\n" + e);
            }

            return result;
        }
        #endregion

        #region Waiter
        private static void Waiter()
        {
            while (!_ct.IsCancellationRequested)
            {
                _waitForInputFlag.WaitOne();
                Console.ReadLine();
                _gotInputFlag.Set();
            }
        }
        private static bool WaitForInput(int timeoutTime = Timeout.Infinite)
        {
            if (timeoutTime <= 0) return false;
            _waitForInputFlag.Set();
            return _gotInputFlag.WaitOne(timeoutTime);
        }
        #endregion

        #region IDisposable Support
        private bool _disposedValue;
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        ~Scraper() { Dispose(false); }

        private void Dispose(bool disposing)
        {
            if (_disposedValue) return;
            if (disposing)
            {
                _cts.Cancel();
                _cts.Dispose();
                _waitForInputFlag.Dispose();
                _gotInputFlag.Dispose();
            }
            _courseList.Clear();
            _sectionList.Clear();
            _courseParameters.Clear();
            _sectionParameters.Clear();
            _courseList = null;
            _sectionList = null;
            _courseParameters = null;
            _sectionParameters = null;

            _disposedValue = true;
        }
        #endregion
    }
}