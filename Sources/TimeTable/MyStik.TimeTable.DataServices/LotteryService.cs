﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;
using log4net.Repository.Hierarchy;
using MyStik.TimeTable.Data;
using MyStik.TimeTable.DataServices.Lottery;

namespace MyStik.TimeTable.DataServices
{
    public class LotteryService
    {
        private TimeTableDbContext db = new TimeTableDbContext();
        private ILog logger = LogManager.GetLogger("Lottery");

        /*
        public void RunLottery()
        {
            try
            {
                var semester = new SemesterService().GetNewestSemester();

                var lotteryService = new LotteryService();

                logger.InfoFormat("Starte Verlosung für Semester {0}", semester.Name);

                var wpmList = lotteryService.GetLottery(semester);

                foreach (var wpm in wpmList)
                {
                    lotteryService.RunLotteryForCourse(wpm);
                }

                logger.InfoFormat("Verlosung für Semester {0} beendet", semester.Name);
            }
            catch (Exception ex)
            {
                logger.FatalFormat("Abbruch wegen Fehler: {0}", ex.Message);
            }
        }
        */

        public void RunLottery(Guid id)
        {
            var lottery = GetLottery(id);
            if (lottery == null)
            {
                logger.FatalFormat("Verlosung [{0}] existiert nicht", id);
                return;
            }

            // schauen, ob es passt
            var today = DateTime.Today;

            if (lottery.FirstDrawing > today)
            {
                logger.InfoFormat("Verlosung {0} startet erst am {1}", lottery.Name, lottery.FirstDrawing.ToShortDateString());
                return;
            }

            if (lottery.LastDrawing < today)
            {
                logger.InfoFormat("Verlosung {0} wurde beendet am {1}", lottery.Name, lottery.LastDrawing.ToShortDateString());
                return;
            }
            
            try
            {
                var wpmList = GetLotteryCourseList(id);

                logger.InfoFormat("Starte Verlosung {0} für {1} Kurse", lottery.Name, wpmList.Count);

                foreach (var wpm in wpmList)
                {
                    RunLotteryForCourse(wpm);
                }

                logger.InfoFormat("Verlosung {0} beendet", lottery.Name);
            }
            catch (Exception ex)
            {
                logger.FatalFormat("Abbruch der Verlosung {0} wegen Fehler: {1}", lottery.Name, ex.Message);
            }
        }



        public ICollection<Course> GetLottery(Semester semester)
        {
            var wpmList = db.Activities.OfType<Course>().Where(c =>
                c.Occurrence.LotteryEnabled &&
                c.SemesterGroups.Any(g => g.Semester.Id == semester.Id)).ToList();

            
            logger.DebugFormat("Es wurden {0} Kurse für die Verlosung im {1} gefunden", wpmList.Count, semester.Name);
            
            return wpmList;
        }

        public Data.Lottery GetLottery(Guid lotteryId)
        {
            return db.Lotteries.SingleOrDefault(l => l.Id == lotteryId);
        }



        public ICollection<Course> GetLotteryCourseList(Guid lotteryId)
        {
            var courseList = new List<Course>();

            var lottery = db.Lotteries.SingleOrDefault(l => l.Id == lotteryId);

            if (lottery != null)
            {
                courseList.AddRange(
                    lottery.Occurrences.Select(
                        occurrence => db.Activities.OfType<Course>().SingleOrDefault(
                            c => c.Occurrence.Id == occurrence.Id)).Where(course => course != null));
            }

            return courseList;
        }



        public CourseDrawingReportModel RunLotteryForCourse(Course wpm)
        {
            var report = new CourseDrawingReportModel
            {
                Course = wpm
            };


            foreach (var subscription in wpm.Occurrence.Subscriptions)
            {
                string text;
                if (subscription.IsConfirmed)
                {
                    text = "TN";
                }
                else
                {
                    if (subscription.OnWaitingList)
                    {
                        text = string.Format("WL ({0})", subscription.LapCount);
                    }
                    else
                    {
                        text = "R";
                    }
                }

                var subReport = new SuscriptionDrawingReportModel
                {
                    Subscription = subscription,
                    StateBeforeDrawing = text,
                    LapCountBeforeDrawing = subscription.LapCount
                };

                report.Subscriptions.Add(subReport);
            }


            logger.Debug("####################################");
            logger.DebugFormat("Starte Verlosung für [{0}]", wpm.ShortName);
            if (wpm.Occurrence.UseGroups)
            {
                logger.DebugFormat("Verlosung nach Semestergruppen");
            }
            else
            {
                logger.DebugFormat("Verlosung nach Gesamtanzahl");
            }

            // Aktuelle Teilnehmerliste und Warteliste ermitteln
            var waitingList = wpm.Occurrence.Subscriptions.Where(s => s.OnWaitingList == true).ToList();
            var participentList = wpm.Occurrence.Subscriptions.Where(s => s.OnWaitingList == false).ToList();


            // Alle die in der Warteliste drin sind um eine Runde nach oben bringen
            // Interne Prio der Warteliste, wer länger wartet, hat bessere Chancen, weil zuerst verlost
            foreach (var subscription in waitingList)
            {
                subscription.LapCount++;
            }

            // alle die nicht bestätigt haben zurück auf die Warteliste und dort ans Ende
            var notAccepted = participentList.Where(s => s.IsConfirmed == false).ToList();

            var n = wpm.Occurrence.Subscriptions.Count;
            var n1 = participentList.Count - notAccepted.Count;
            var n2 = notAccepted.Count;
            var n3 = waitingList.Count;

            logger.DebugFormat("Status vor Beginn der Verlosung:");
            logger.DebugFormat("Eintragungen gesamt [{0}]", n);
            logger.DebugFormat("davon Teilnehmer [{0}]", n1);
            logger.DebugFormat("davon Reservierungen [{0}]", n2);
            logger.DebugFormat("Auf Warteliste [{0}]", n3);
            
            foreach (var subscription in notAccepted)
            {
                subscription.OnWaitingList = true;
                subscription.LapCount = 0;

                // die entsprechenden Listen müssen angepasst werden.
                waitingList.Add(subscription);
                participentList.Remove(subscription);

                // Report
                var subReport = report.Subscriptions.SingleOrDefault(s => s.Subscription.Id == subscription.Id);
                subReport.Remark = "<p>Reservierung nicht angenommen. Zurückgesetzt.</p>";
            }

            // diesen Stand vor der Durchführung der Verlosung sichern
            // wer sich während der Verlosung einträgt rutscht somit ans Ende
            db.SaveChanges();


            n = wpm.Occurrence.Subscriptions.Count;
            n1 = participentList.Count;
            n2 = 0;
            n3 = waitingList.Count;
            
            // Das sind die Anzahl der Kandidaten
            var candidates = n3;
            var available = 0;

            logger.Debug("Nach Stornierung der Reservierungen");
            logger.DebugFormat("Eintragungen gesamt [{0}]", n);
            logger.DebugFormat("davon Teilnehmer [{0}]", n1);
            logger.DebugFormat("Auf Warteliste [{0}]", n3);


            if (wpm.Occurrence.UseGroups)
            {
                // Für jede Gruppe separat
                // hier wird jetzt auch die Gruppenzugehörigkeit berücksichtig!
                foreach (var group in wpm.Occurrence.Groups)
                {
                    logger.Debug("------");
                    logger.DebugFormat("Zusammenstellung der Lostöpfe für Kapazitätsgruppe mit [{0}] Plätzen", group.Capacity);
                    foreach (var semesterGroup in group.SemesterGroups)
                    {
                        logger.DebugFormat("Zugehörige Semestergruppe [{0}]", semesterGroup.FullName);
                    }
                    var groupParticipantList = GetSubscriptionsForGroup(participentList, group, report);
                    var groupWaitingList = GetSubscriptionsForGroup(waitingList, group, report);
                    logger.Debug("------");

                    available += LotteryForGroup(groupWaitingList, groupParticipantList, group.Capacity);
                }
            }
            else
            {
                // es wird auf Gesamtebene verlost
                // d.h. hier müssen zuerst alle aus der Warteliste entfernt werden, die zu einem anderen
                // Studiengang gehören

                var validWaitingList = new List<OccurrenceSubscription>();

                foreach (var group in wpm.Occurrence.Groups)
                {
                    logger.Debug("------");
                    logger.Debug("Zusammenstellung des Lostopfs für alle Kapazitätsgruppen");
                    foreach (var semesterGroup in group.SemesterGroups)
                    {
                        logger.DebugFormat("Zugehörige Semestergruppe [{0}]", semesterGroup.FullName);
                    }

                    var groupWaitingList = GetSubscriptionsForGroup(waitingList, group, report);
                    validWaitingList.AddRange(groupWaitingList);

                    logger.Debug("------");
                }

                available += LotteryForGroup(validWaitingList, participentList, wpm.Occurrence.Capacity);
            }

            db.SaveChanges();

            n = wpm.Occurrence.Subscriptions.Count;
            n1 = wpm.Occurrence.Subscriptions.Where(s => s.OnWaitingList == true && s.IsConfirmed == true).ToList().Count;
            n2 = wpm.Occurrence.Subscriptions.Where(s => s.OnWaitingList == true && s.IsConfirmed == false).ToList().Count;
            n3 = wpm.Occurrence.Subscriptions.Where(s => s.OnWaitingList == false).ToList().Count;

            logger.DebugFormat("Status vor Nach der Verlosung:");
            logger.DebugFormat("Eintragungen gesamt [{0}]", n);
            logger.DebugFormat("davon Teilnehmer [{0}]", n1);
            logger.DebugFormat("davon Reservierungen [{0}]", n2);
            logger.DebugFormat("Auf Warteliste [{0}]", n3);

            logger.Debug("####################################");

            logger.InfoFormat("Verlosung für [{0}]: {1} freie Plätze unter {2} Kandidaten verlost", wpm.ShortName, available, candidates);


            foreach (var subscription in wpm.Occurrence.Subscriptions)
            {
                string text;
                if (subscription.IsConfirmed)
                {
                    text = "TN";
                }
                else
                {
                    if (subscription.OnWaitingList)
                    {
                        text = string.Format("WL ({0})", subscription.LapCount);
                    }
                    else
                    {
                        text = "R";
                    }
                }

                var subReport = report.Subscriptions.SingleOrDefault(s => s.Subscription.Id == subscription.Id);
                subReport.StateAfterDrawing = text;
                subReport.LapCountAfterDrawing = subscription.LapCount;
            }


            return report;
        }

        private int LotteryForGroup(List<OccurrenceSubscription> waitingList, List<OccurrenceSubscription> participantList, int capacity)
        {
            logger.Debug("Durchführung der Verlosung");
            logger.DebugFormat("Kapazität gesamt {0}", capacity);
            logger.DebugFormat("Anzahl Kandidaten {0}", waitingList.Count);
            logger.DebugFormat("Anzahl Teilnehmer bisher [{0}]", participantList.Count);

            var nAvailable = capacity - participantList.Count;

            // Bei einer leeren Warteliste muss nicht verlost werden
            if (waitingList.Count == 0)
            {
                logger.Debug("Warteliste ist leer. Keine Aktion.");
                return nAvailable;
            }

            // Wenn es keine gruppen gibt, dann wird dies auch nicht berücksichtigt
            logger.DebugFormat("Anzahl Restplätze [{0}]", nAvailable);

            // ohne Beschränkung => alle gehen rein
            if (capacity < 0 || nAvailable >= waitingList.Count)
            {
                logger.Debug("Anzahl verfügbare Plätze größer als Einträge Warteliste. Alle erhalten eine Reservierung.");
                // alle gehen rein
                foreach (var waiting in waitingList)
                {
                    waiting.OnWaitingList = false;
                    waiting.IsConfirmed = false;
                }
            }
            else
            {
                logger.Debug("Anzahl verfügbarer Plätze kleiner als Warteliste. Verlosung durchführen.");
                // Es muss verlost werden, aber ohne Gruppen
                var maxLapCount = waitingList.Max(s => s.LapCount);
                var lap = maxLapCount;
                logger.DebugFormat("In der Warteliste befinden sich Einträge aus {0} Runden", lap);
                bool go = true;
                while (lap >= 0 && go)
                {
                    var candidates = waitingList.Where(s => s.LapCount == lap).ToList();

                    var nLeft = capacity - participantList.Count;

                    logger.DebugFormat("Verlosungsrunde [{0}]", lap);
                    logger.DebugFormat("Anzahl freier Plätze [{0}]", nLeft);
                    logger.DebugFormat("Anzahl Kandidaten [{0}]", candidates.Count);
                    logger.DebugFormat("Anzahl Teilnehmer bis jetzt {0}", participantList.Count);

                    if (nLeft >= candidates.Count)
                    {
                        logger.Debug("Anzahl freier Plätze größer als Anzahl Kandidaten. Alle erhalten eine Reservierung.");
                        // alle gehen rein
                        foreach (var candidate in candidates)
                        {
                            candidate.OnWaitingList = false;
                            candidate.IsConfirmed = false;

                            // zur Teilnehmerliste hinzufügen ist hier erforderlich
                            // damit im nächsten Durchlauf die Anzahl passt
                            participantList.Add(candidate);
                        }

                        if (nLeft == candidates.Count)
                        {
                            go = false;
                        }
                    }
                    else
                    {
                        // Verlosen
                        logger.Debug("Verlose Plätze");
                        // Schüttelt die Liste durch
                        candidates.Shuffle();

                        // nimm die ersten n Kandidaten aus der Zufallsliste
                        for (var i = 0; i < nLeft; i++)
                        {
                            var candiate = candidates[i];
                            candiate.OnWaitingList = false;
                            candiate.IsConfirmed = false;
                        }

                        go = false;
                    }

                    lap--;
                }
            }

            return nAvailable;
        }

        public List<OccurrenceSubscription> GetSubscriptionsForGroup(ICollection<OccurrenceSubscription> allSubscriptions, 
            OccurrenceGroup group, CourseDrawingReportModel report)
        {
            if (!group.Occurrence.UseExactFit)
            {
                logger.DebugFormat("Suche nach Studiengang");
            }
            else
            {
                logger.DebugFormat("Suche nach exakter Semestergruppe");
            }

            var subList = new List<OccurrenceSubscription>();


            foreach (var subscription in allSubscriptions)
            {
                var subReport = report.Subscriptions.SingleOrDefault(s => s.Subscription.Id == subscription.Id);

                var bFound = false;

                foreach (var semesterGroup in group.SemesterGroups)
                {
                    if (!bFound)
                    {
                        // ist der Student im Semester dieser Gruppe eingeschrieben?
                        // zur Sicherheit, falls er in mehreren Gruppen drin sein sollte
                        var semSub = db.Subscriptions.OfType<SemesterSubscription>()
                            .FirstOrDefault(s => s.SemesterGroup.Semester.Id == semesterGroup.Semester.Id &&
                                                 s.UserId.Equals(subscription.UserId));

                        // er ist eingeschrieben
                        // jetzt nach den Anforderungen der Occurrence weiter
                        if (semSub != null)
                        {
                            bool isInGroup;
                            if (!group.Occurrence.UseExactFit)
                            {
                                // Hier genügt der Studiengang
                                isInGroup =
                                    group.SemesterGroups.Any(
                                        g =>
                                            g.CapacityGroup.CurriculumGroup.Curriculum.Id ==
                                            semSub.SemesterGroup.CapacityGroup.CurriculumGroup.Curriculum.Id);
                                /*
                            if (isInGroup == false)
                            {
                                logger.DebugFormat("Suche nach Studienprogramm erfolglos: {0}", semSub.SemesterGroup.FullName);
                            }
                             */
                                if (!isInGroup)
                                {
                                    subReport.Remark += string.Format("<p>Studiengang passt nicht exakt: Eingetragen in {0}</p>", semSub.SemesterGroup.FullName);
                                }
                            }
                            else
                            {
                                // Die Semestergruppe muss exakt passen
                                isInGroup = group.SemesterGroups.Any(g => g.Id == semSub.SemesterGroup.Id);
                                if (!isInGroup)
                                {
                                    subReport.Remark += string.Format("<p>Semestergruppe passt nicht exakt: Eingetragen in {0}</p>", semSub.SemesterGroup.FullName);
                                }
                            }

                            // nur hinzufügen, wenn noch nicht drin
                            if (isInGroup && !subList.Contains(subscription))
                            {
                                subList.Add(subscription);
                                bFound = true;
                            }

                        }
                        else
                        {
                            logger.DebugFormat("Keine Angabe zu Semestergruppe - wird ignoriert");
                            subReport.Remark += "<p>Keine Angabe zu Semestergruppe - wird ignoriert</p>";
                        }
                    }
                }

                // hier noch der Fehlertext
            }

            logger.DebugFormat("Anzahl Eintragungen gesamt [{0}]", allSubscriptions.Count);
            logger.DebugFormat("Anzahl passender Eintragungen [{0}]", subList.Count);

            return subList;
        }

    }
}