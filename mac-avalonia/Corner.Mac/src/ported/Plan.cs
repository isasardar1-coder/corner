// Ported from corner/src/Plan.cs (WPF) to Avalonia: tools/port_ui.py, then fixed by hand.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using IO = System.IO;

namespace Corner
{
    partial class Shell
    {
        /// "Now", overridable for tests with CORNER_NOW=HH:mm.
        static DateTime Now()
        {
            TimeSpan t; string s = Environment.GetEnvironmentVariable("CORNER_NOW");
            return s != null && TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out t) ? DateTime.Today + t : DateTime.Now;
        }

        void UpdateInputPreview()
        {
            string en, kind, title; int mins; DateTime due; bool ht;
            string text = TaskTags.Extract(input.Text, out en, out mins, out kind);
            var parts = new List<string>();
            if (en != null) parts.Add(TaskTags.Short(en));
            if (kind == "physical") parts.Add("phys");
            if (mins > 0) parts.Add(TaskTags.Len(mins));
            if (When.Parse(text, out title, out due, out ht)) parts.Add(When.Label(due, ht));
            inputPreview.Text = string.Join(" · ", parts);
            inputChip.IsVisible = parts.Count > 0 ? true : false;
        }


        /// Marks a task that came from a routine.
        static Control RoutineMark()
        {
            return new TextBlock { Text = "↻", FontSize = Z(11.5), Foreground = T.Faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 2), IsHitTestVisible = false };
        }

        // ------------------------------------------------------------ routines -> today's tasks

        /// First open of the day: drop yesterday's unfinished routine tasks. Routines join the day only when you send
        /// them, with "Add to today" in the Routines list (AddRoutinesToToday).
        void EnsureRoutines(DateTime day)
        {
            LogDays(); // yesterday into days.csv first, while its unfinished routine tasks are still here (Stats.cs)
            Routines.Load();
            Routines.OfferNap();
            string key = day.ToString("yyyy-MM-dd");
            if (data.Tasks.RemoveAll(t => t.Routine != null && !t.Done && t.RoutineDay != key) > 0) Save();
        }

        /// True when the routine is already among today's tasks (done or not).
        bool RoutineInToday(Routine r)
        {
            string key = SleepLog.Today(Now()).ToString("yyyy-MM-dd");
            return data.Tasks.Any(t => t.Routine == r.Id && t.RoutineDay == key);
        }

        /// Sends routines to today's task list, each with its effort and length, so Plan my day can place them.
        /// A routine already in today's list is not added again. Returns how many tasks were added.
        int AddRoutinesToToday(IEnumerable<Routine> routines)
        {
            string key = SleepLog.Today(Now()).ToString("yyyy-MM-dd");
            int added = 0;
            foreach (var r in routines.ToList())
            {
                if (RoutineInToday(r)) continue;
                int n = Math.Max(1, r.CheckIns);
                for (int k = 1; k <= n; k++, added++)
                    data.Tasks.Add(new TaskItem
                    {
                        Id = Guid.NewGuid().ToString("N"), Title = r.Title + (n > 1 ? " (" + k + "/" + n + ")" : ""),
                        Energy = TaskTags.Level(r.Energy), Kind = r.Kind == "physical" ? "physical" : null, Minutes = r.Minutes,
                        Routine = r.Id, RoutineDay = key, Part = n > 1 ? k : 0
                    });
            }
            if (added > 0) { proposal = null; Save(); Rebuild(); DrawEnergy(); }
            return added;
        }


        // ------------------------------------------------------------ Plan

        class PlanUndo { public string Id, Due; public bool HasTime, Notified, Planned; }
        List<PlanItem> proposal;   // shown, not yet applied
        List<PlanUndo> planUndo;   // what the last applied plan replaced
        bool routinesOpen;
        string editRoutine;


        void MakePlan() { MakePlan(false); }

        void MakePlan(bool keepStarted)
        {
            if (energy == null) return;
            proposal = Planner.Plan(data.Tasks, energy, Routines.Data != null ? Routines.Data.Routines : null, Now(), keepStarted);
            DrawEnergy();
        }

        void ApplyPlan()
        {
            if (proposal == null) return;
            planUndo = new List<PlanUndo>();
            foreach (var it in proposal.Where(x => x.Placed))
            {
                var t = data.Tasks.FirstOrDefault(x => x.Id == it.Id);
                if (t == null || t.Done) continue;
                planUndo.Add(new PlanUndo { Id = t.Id, Due = t.Due, HasTime = t.HasTime, Notified = t.Notified, Planned = t.Planned });
                t.SetDue(it.Start, true);
                t.Planned = true;
            }
            proposal = null;
            Save(); Rebuild(); DrawEnergy();
        }

        void UndoPlan()
        {
            if (planUndo == null) return;
            foreach (var u in planUndo)
            {
                var t = data.Tasks.FirstOrDefault(x => x.Id == u.Id);
                if (t == null) continue;
                t.Due = u.Due; t.HasTime = u.HasTime; t.Notified = u.Notified; t.Planned = u.Planned;
            }
            planUndo = null;
            Save(); Rebuild(); DrawEnergy();
        }

        // ------------------------------------------------------------ the Routines list

        const string RoutineHelp = "After the title, type any of: 20m · !low !med !high · !phys · daily, weekdays or mon,wed,fri · after 2 · x3 for check-ins · !early · !dip · !off";

    }
}
