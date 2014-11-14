﻿using System;
using System.Collections.Generic;
using SmartQuant;
using SmartQuant.Charting;
using SmartQuant.ChartViewers;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
#if GTK
using Gtk;
#else
using System.Windows.Forms;
#endif

namespace SmartQuant.Controls.BarChart
{
    public class BarChart : FrameworkControl, IGroupListener
    {
        private Dictionary<int, GroupItem> table = new Dictionary<int, GroupItem>();
        private Dictionary<object, List<GroupEvent>> eventsBySelectorKey = new Dictionary<object, List<GroupEvent>>();
        private Dictionary<int, List<Group>> orderedGroupTable = new Dictionary<int, List<Group>>();
        private Dictionary<object, Dictionary<int, List<Group>>> drawnGroupTable = new Dictionary<object, Dictionary<int, List<Group>>>();
        private long barSize = 60;
        private bool freezeUpdate;
        private DateTime firstDateTime;
        private Chart chart;
        private ComboBox cbxSelector;

        public PermanentQueue<Event> Queue { get; private set; }

        public BarChart()
        {
            InitComponent();
        }

        private void OnFrameworkCleared(object sender, FrameworkEventArgs args)
        {
            InvokeAction(delegate
            {
                #if GTK
                this.cbxSelector.Model = null;
                #else
                this.cbxSelector.Items.Clear();
                #endif
                this.Reset(false);
                this.chart.UpdatePads();
            });
            this.eventsBySelectorKey.Clear();
            this.eventsBySelectorKey[""] = new List<GroupEvent>();
        }

        protected override void OnInit()
        {
            Queue = new PermanentQueue<Event>();
            Queue.AddReader(this);
            Reset(true);
            this.framework.EventManager.Dispatcher.FrameworkCleared += new FrameworkEventHandler(this.OnFrameworkCleared);
            this.framework.GroupDispatcher.AddListener(this);
            this.eventsBySelectorKey[""] = new List<GroupEvent>();
        }

        protected override void OnClosing(CancelEventArgs args)
        {
            Queue.RemoveReader(this);
        }

        public bool OnNewGroup(Group group)
        {
            if (!group.Fields.ContainsKey("Pad"))
                return false;
            this.table[group.Id] = new GroupItem(group);
            List<Group> list = null;
            int key = (int)group.Fields["Pad"].Value;
            if (!this.orderedGroupTable.TryGetValue(key, out list))
            {
                list = new List<Group>();
                this.orderedGroupTable[key] = list;
            }
            list.Add(group);
            InvokeAction(delegate
            {
                if (!group.Fields.ContainsKey("SelectorKey"))
                    return;
                string str = (string)group.Fields["SelectorKey"].Value;
                #if GTK
                #else
                if (this.cbxSelector.Items.Contains((object)str))
                    return;
                this.cbxSelector.Items.Add((object)str);
                this.eventsBySelectorKey[(object)str] = new List<GroupEvent>();
                this.freezeUpdate = true;
                if (this.cbxSelector.Items.Count == 1)
                    this.cbxSelector.SelectedIndex = 0;
                #endif
                this.freezeUpdate = false;
            });
            return true;
        }

        public void OnNewGroupEvent(GroupEvent groupEvent)
        {
            GroupItem groupItem = this.table[groupEvent.Group.Id];
            Tuple<Viewer, object> tuple = (Tuple<Viewer, object>)null;
            groupItem.Table.TryGetValue((int)groupEvent.Obj.TypeId, out tuple);
            switch (groupEvent.Obj.TypeId)
            {
                case DataObjectType.Bar:
                    object obj1;
                    if (tuple == null)
                    {
                        obj1 = (object)new BarSeries("", "", -1);
                        int padNumber = groupItem.PadNumber;
                        this.EnsurePadExists(padNumber, groupItem.Format);
                        int viewerIndex = this.GetViewerIndex(groupEvent.Group, padNumber);
                        Viewer viewer = this.chart.Pads[padNumber].Insert(viewerIndex, (object)(obj1 as BarSeries));
                        this.chart.Pads[padNumber].Legend.Add(groupEvent.Group.Name, Color.Black);
                        groupItem.Table.Add((int)groupEvent.Obj.TypeId, new Tuple<Viewer, object>(viewer, obj1));
                    }
                    else
                        obj1 = (object)(tuple.Item2 as BarSeries);
                    (obj1 as BarSeries).Add(groupEvent.Obj as Bar);
                    break;
                case DataObjectType.Fill:
                    object obj2;
                    if (tuple == null)
                    {
                        obj2 = (object)new FillSeries("");
                        int padNumber = groupItem.PadNumber;
                        this.EnsurePadExists(padNumber, groupItem.Format);
                        int viewerIndex = this.GetViewerIndex(groupEvent.Group, padNumber);
                        Viewer viewer = this.chart.Pads[padNumber].Insert(viewerIndex, obj2);
                        groupItem.Table.Add((int)groupEvent.Obj.TypeId, new Tuple<Viewer, object>(viewer, obj2));
                    }
                    else
                        obj2 = (object)(tuple.Item2 as FillSeries);
                    (obj2 as FillSeries).Add(groupEvent.Obj as Fill);
                    break;
                case DataObjectType.TimeSeriesItem:
                    object obj3;
                    if (tuple == null)
                    {
                        obj3 = (object)new TimeSeries();
                        int padNumber = groupItem.PadNumber;
                        this.EnsurePadExists(padNumber, groupItem.Format);
                        int viewerIndex = this.GetViewerIndex(groupEvent.Group, padNumber);
                        Viewer viewer = this.chart.Pads[padNumber].Insert(viewerIndex, obj3);
                        foreach (KeyValuePair<string, GroupField> keyValuePair in groupEvent.Group.Fields)
                            viewer.Set(obj3, keyValuePair.Value.Name, keyValuePair.Value.Value);
                        if (groupEvent.Group.Fields.ContainsKey("Color"))
                            this.chart.Pads[padNumber].Legend.Add(groupEvent.Group.Name, (Color)groupEvent.Group.Fields["Color"].Value);
                        else
                            this.chart.Pads[padNumber].Legend.Add(groupEvent.Group.Name, Color.Black);
                        groupItem.Table.Add((int)groupEvent.Obj.TypeId, new Tuple<Viewer, object>(viewer, obj3));
                    }
                    else
                        obj3 = (object)(tuple.Item2 as TimeSeries);
                    (obj3 as TimeSeries).Add((groupEvent.Obj as TimeSeriesItem).DateTime, (groupEvent.Obj as TimeSeriesItem).Value);
                    break;
            }
        }

        private int GetViewerIndex(Group group, int padNumber)
        {
            List<Group> list1 = this.orderedGroupTable[padNumber];
            List<Group> list2;
            Dictionary<int, List<Group>> dictionary;
            var selected = (string)GetComboBoxSelected();
            if (!this.drawnGroupTable.TryGetValue(selected, out dictionary))
            {
                dictionary = new Dictionary<int, List<Group>>();
                this.drawnGroupTable[selected] = dictionary;
            }
            if (!dictionary.TryGetValue(padNumber, out list2))
            {
                dictionary[padNumber] = new List<Group>()  { group };
                return 0;
            }
            else
            {
                bool flag = false;
                for (int i = 0; i < list2.Count; ++i)
                {
                    Group group1 = list2[i];
                    if (group1 != group && list1.IndexOf(group) < list1.IndexOf(group1))
                    {
                        list2.Insert(i, group1);
                        return i;
                    }
                }
                if (flag)
                    return 0;
                list2.Add(group);
                return list2.Count - 1;
            }
        }

        public void OnNewGroupUpdate(GroupUpdate groupUpdate)
        {
            if (InvokeRequired)
            {
                Invoke(new System.Action(() =>
                {
                    OnNewGroupUpdate(groupUpdate);
                }));
            }
            else
            {
                var groupItem = this.table[groupUpdate.GroupId];
                if (groupUpdate.FieldName == "Pad")
                {
                    int padNumber = groupItem.PadNumber;
                    string format = groupItem.Format;
                    int newPad = (int)groupUpdate.Value;
                    string labelFormat = (string)groupUpdate.Value;
                    foreach (var keyValuePair in groupItem.Table)
                    {
                        this.chart.Pads[padNumber].Remove(keyValuePair.Value.Item2);
                        this.EnsurePadExists(newPad, labelFormat);
                        this.chart.Pads[newPad].Add(keyValuePair.Value.Item2);
                    }
                    groupItem.PadNumber = newPad;
                    groupItem.Format = labelFormat;
                }
                if (!(groupUpdate.FieldName == "Color"))
                    return;
                Color color = (Color)groupUpdate.Value;
                foreach (var keyValuePair in groupItem.Table)
                {
                    if (keyValuePair.Value.Item1 is TimeSeriesViewer)
                        (keyValuePair.Value.Item1 as TimeSeriesViewer).Color = color;
                }
                this.chart.UpdatePads();
            }
        }

        private void EnsurePadExists(int newPad, string labelFormat)
        {
            for (int count = this.chart.Pads.Count; count <= newPad; ++count)
            {
                Pad pad = this.AddPad();
                pad.RegisterViewer(new BarSeriesViewer());
                pad.RegisterViewer(new TimeSeriesViewer());
                pad.RegisterViewer(new FillSeriesViewer());
                pad.RegisterViewer(new TickSeriesViewer());
                pad.AxisBottom.Type = EAxisType.DateTime;
            }
            this.chart.Pads[newPad].MarginBottom = 0;
            this.chart.Pads[newPad].AxisBottom.Type = EAxisType.DateTime;
            this.chart.Pads[newPad].AxisBottom.LabelEnabled = true;
            this.chart.Pads[newPad].AxisLeft.LabelFormat = labelFormat;
            this.chart.Pads[newPad].AxisRight.LabelFormat = labelFormat;
        }

        private Pad AddPad()
        {
            double num1 = 0.15;
            double Y1 = 0.0;
            foreach (Pad pad in this.chart.Pads)
            {
                double canvasHeight = pad.CanvasHeight;
                double num2 = canvasHeight - num1 * canvasHeight / (1.0 - num1);
                pad.CanvasY1 = Y1;
                pad.CanvasY2 = Y1 + num2;
                Y1 = pad.CanvasY2;
            }
            Pad pad1 = this.chart.Pads[0];
            Pad pad2 = this.chart.Pads[this.chart.Pads.Count - 1];
            Pad pad3 = this.chart.AddPad(0.0, Y1, 1.0, 1.0);
            pad3.TitleEnabled = pad2.TitleEnabled;
            pad3.BackColor = pad1.BackColor;
            pad3.BorderColor = pad1.BorderColor;
            pad3.BorderEnabled = pad1.BorderEnabled;
            pad3.BorderWidth = pad1.BorderWidth;
            pad3.ForeColor = pad1.ForeColor;
            pad2.AxisBottom.LabelEnabled = false;
            pad2.AxisBottom.Height = 0;
            pad3.AxisBottom.LabelEnabled = true;
            pad3.AxisBottom.LabelFormat = "MMMM yyyy";
            pad3.AxisBottom.Type = EAxisType.DateTime;
            pad2.MarginBottom = 0;
            pad3.MarginBottom = 10;
            pad3.AxisBottom.TitleEnabled = pad2.AxisBottom.TitleEnabled;
            pad3.MarginTop = 0;
            pad3.MarginLeft = pad1.MarginLeft;
            pad3.MarginRight = pad1.MarginRight;
            pad3.AxisLeft.LabelEnabled = pad1.AxisLeft.LabelEnabled;
            pad3.AxisLeft.TitleEnabled = pad1.AxisLeft.TitleEnabled;
            pad3.AxisLeft.Width = 50;
            pad3.Width = pad2.Width;
            pad3.AxisRight.LabelEnabled = pad2.AxisRight.LabelEnabled;
            pad3.AxisBottom.Type = pad2.AxisBottom.Type;
            pad3.YAxisLabelFormat = "F5";
            pad3.LegendEnabled = pad1.LegendEnabled;
            pad3.LegendPosition = pad1.LegendPosition;
            pad3.LegendBackColor = pad1.LegendBackColor;
            pad3.AxisBottom.LabelColor = pad1.AxisBottom.LabelColor;
            pad3.AxisRight.LabelColor = pad1.AxisRight.LabelColor;
            pad3.XGridColor = pad1.XGridColor;
            pad3.YGridColor = pad1.YGridColor;
            pad3.XGridDashStyle = pad1.XGridDashStyle;
            pad3.YGridDashStyle = pad1.YGridDashStyle;
            pad3.SetRangeX(pad1.XRangeMin, pad1.XRangeMax);
            pad3.AxisBottom.SetRange(pad1.AxisBottom.Min, pad1.AxisBottom.Max);
            pad3.AxisTop.SetRange(pad1.AxisTop.Min, pad1.AxisTop.Max);
            pad3.AxisBottom.Zoomed = pad1.AxisBottom.Zoomed;
            pad3.AxisTop.Zoomed = pad1.AxisTop.Zoomed;
            pad3.AxisBottom.Enabled = true;
            pad3.AxisBottom.Height = 20;
            pad3.AxisBottom.Type = EAxisType.DateTime;
            pad3.AxisBottom.LabelFormat = "d";
            pad3.AxisBottom.LabelEnabled = true;
            return pad3;
        }

        private void MoveWindow(DateTime dateTime)
        {
            if (this.firstDateTime == dateTime)
            {
                this.chart.SetRangeX((double)(dateTime.Ticks - this.barSize * 10000000L * 30L), (double)dateTime.Ticks);
                this.firstDateTime = dateTime;
            }
            else
                this.chart.SetRangeX((double)this.firstDateTime.Ticks, (double)dateTime.Ticks);
            this.chart.UpdatePads();
        }

        public void UpdateGUI()
        {
            if (FrameworkControl.UpdatedSuspened && this.framework.Mode != FrameworkMode.Realtime)
                return;
            Event[] eventArray = this.Queue.DequeueAll((object)this);
            if (eventArray == null)
                return;
            List<GroupEvent> list1 = new List<GroupEvent>();
            for (int i = 0; i < eventArray.Length; ++i)
            {
                Event e = eventArray[i];
                if (e.TypeId == EventType.GroupEvent)
                {
                    GroupEvent groupEvent = e as GroupEvent;
                    object key = "";
                    GroupField groupField = null;
                    var selected = (string)GetComboBoxSelected();
                    if (groupEvent.Group.Fields.TryGetValue("SelectorKey", out groupField))
                        key = groupField.Value;
                    if (selected == null && string.IsNullOrEmpty(key.ToString()) || selected.Equals(key))
                        list1.Add(groupEvent);
                    List<GroupEvent> list2;
                    if (this.eventsBySelectorKey.TryGetValue(key, out list2))
                        list2.Add(groupEvent);
                }
                else if (e.TypeId == EventType.OnFrameworkCleared)
                    list1.Clear();
            }
            for (int i = 0; i < list1.Count; ++i)
                ProcessEvent(list1[i], i == list1.Count - 1);
        }

        private void ProcessEvent(GroupEvent groupEvent, bool lastEvent)
        {
            this.OnNewGroupEvent(groupEvent);
            if (this.firstDateTime == DateTime.MinValue)
                this.firstDateTime = groupEvent.Obj.DateTime;
            if (!lastEvent)
                return;
            this.MoveWindow(groupEvent.Obj.DateTime);
        }

        private void Reset(bool clearTable)
        {
            if (clearTable)
            {
                this.orderedGroupTable.Clear();
                this.table.Clear();
            }
            else
            {
                foreach (var groupItem in this.table.Values)
                    groupItem.Table.Clear();
            }
            this.drawnGroupTable.Clear();
            this.firstDateTime = DateTime.MinValue;
            this.chart.Clear();
            this.chart.Divide(1, 1);
            Pad pad = this.chart.Pads[0];
            pad.RegisterViewer(new BarSeriesViewer());
            pad.RegisterViewer(new TimeSeriesViewer());
            pad.RegisterViewer(new FillSeriesViewer());
            pad.RegisterViewer(new TickSeriesViewer());
            this.chart.Pads[0].AxisBottom.LabelFormat = "a";
            this.chart.GroupRightMarginEnabled = true;
            this.chart.GroupLeftMarginEnabled = true;
            this.chart.GroupZoomEnabled = true;
            this.chart.Pads[0].MarginBottom = 0;
            this.chart.Pads[this.chart.Pads.Count - 1].AxisBottom.Type = EAxisType.DateTime;
            for (int index = 0; index < this.chart.Pads.Count; ++index)
            {
                this.chart.Pads[index].MarginRight = 10;
                this.chart.Pads[index].XAxisLabelEnabled = index == this.chart.Pads.Count - 1;
                this.chart.Pads[index].XAxisTitleEnabled = false;
                this.chart.Pads[index].TitleEnabled = false;
                this.chart.Pads[index].BorderEnabled = false;
                this.chart.Pads[index].BackColor = Color.FromKnownColor(KnownColor.Control);
                this.chart.Pads[index].AxisLeft.Width = 50;
                this.chart.Pads[index].AxisBottom.GridDashStyle = DashStyle.Dot;
                this.chart.Pads[index].AxisLeft.GridDashStyle = DashStyle.Dot;
                this.chart.Pads[index].LegendEnabled = true;
                this.chart.Pads[index].LegendPosition = ELegendPosition.TopLeft;
                this.chart.Pads[index].LegendBackColor = Color.White;
                this.chart.Pads[index].AxisBottom.Type = EAxisType.DateTime;
            }
        }

        private void OnSelectorValueChanged(object sender, EventArgs e)
        {
            var selected = (string)GetComboBoxSelected();
            if (this.freezeUpdate)
                return;
            Reset(false);
            var list = this.eventsBySelectorKey[selected];
            for (int i = 0; i < list.Count; ++i)
                this.ProcessEvent(list[i], i == list.Count - 1);
            this.chart.UpdatePads();
        }

        private string GetComboBoxSelected()
        {
            #if GTK
            TreeIter iter;
            if (this.cbxSelector.GetActiveIter(out iter))
                return this.cbxSelector.Model.GetValue(iter, 0).ToString();
            else 
                return String.Empty;
            #else
            return this.cbxSelector.SelectedItem.ToString();
            #endif
        }

        #if GTK
        private void InitComponent()
        {
        this.chart = new Chart();
        this.cbxSelector = new ComboBox();
        this.cbxSelector.Changed += OnSelectorValueChanged;
        VBox vb = new VBox();
        vb.PackStart(this.cbxSelector, false, true, 0);
        vb.PackEnd(this.chart, true, true, 0);
        Add(vb);
        ShowAll();
        }
        #else
        private void InitComponent()
        {
            this.chart = new Chart();
            this.cbxSelector = new ComboBox();
            this.SuspendLayout();
            this.chart.AntiAliasingEnabled = false;
            this.chart.Dock = DockStyle.Fill;
            this.chart.DoubleBufferingEnabled = true;
            this.chart.FileName = (string)null;
            this.chart.GroupLeftMarginEnabled = false;
            this.chart.GroupRightMarginEnabled = false;
            this.chart.GroupZoomEnabled = false;
            this.chart.Location = new Point(0, 21);
            this.chart.Name = "chart";
            this.chart.PadsForeColor = Color.White;
            this.chart.PrintAlign = EPrintAlign.None;
            this.chart.PrintHeight = 400;
            this.chart.PrintLayout = EPrintLayout.Portrait;
            this.chart.PrintWidth = 600;
            this.chart.PrintX = 10;
            this.chart.PrintY = 10;
            this.chart.SessionEnd = TimeSpan.Parse("1.00:00:00");
            this.chart.SessionGridColor = Color.Blue;
            this.chart.SessionGridEnabled = false;
            this.chart.SessionStart = TimeSpan.Parse("00:00:00");
            this.chart.Size = new Size(725, 392);
            this.chart.SmoothingEnabled = false;
            this.chart.TabIndex = 0;
            this.chart.TransformationType = ETransformationType.Empty;
            this.cbxSelector.Dock = DockStyle.Top;
            this.cbxSelector.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cbxSelector.FormattingEnabled = true;
            this.cbxSelector.Location = new Point(0, 0);
            this.cbxSelector.Name = "cbxSelector";
            this.cbxSelector.Size = new Size(725, 21);
            this.cbxSelector.TabIndex = 1;
            this.cbxSelector.SelectedIndexChanged += new EventHandler(OnSelectorValueChanged);
            this.AutoScaleDimensions = new SizeF(6f, 13f);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.Controls.Add((Control)this.chart);
            this.Controls.Add((Control)this.cbxSelector);
            this.Name = "BarChart";
            this.Size = new Size(725, 413);
            this.ResumeLayout(false);
        }
        #endif
    }
}
