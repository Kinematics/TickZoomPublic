using System;
using ZedGraph;
#region Copyright
/*
 * Software: TickZoom Trading Platform
 * Copyright 2009 M. Wayne Walter
 * 
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 * 
 * Business use restricted to 30 days except as otherwise stated in
 * in your Service Level Agreement (SLA).
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, see <http://www.tickzoom.org/wiki/Licenses>
 * or write to Free Software Foundation, Inc., 51 Franklin Street,
 * Fifth Floor, Boston, MA  02110-1301, USA.
 * 
 */
#endregion

namespace TickZoom.Charting
{
	partial class ChartControl
	{
		/// <summary>
		/// Designer variable used to keep track of non-visual components.
		/// </summary>
		private System.ComponentModel.IContainer components = null;
		
		/// <summary>
		/// Disposes resources used by the control.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing) {
				if (components != null) {
					components.Dispose();
				}
			}
			base.Dispose(disposing);
		}
		
		/// <summary>
		/// This method is required for Windows Forms designer support.
		/// Do not change the method contents inside the source code editor. The Forms designer might
		/// not be able to load this method if it was changed manually.
		/// </summary>
		private void InitializeComponent()
		{
            this.components = new System.ComponentModel.Container();
            this.refreshTimer = new System.Windows.Forms.Timer(this.components);
            this.toolStripStatusXY = new System.Windows.Forms.ToolStripStatusLabel();
            this.statusStrip1 = new System.Windows.Forms.StatusStrip();
            this.logTextBox = new System.Windows.Forms.TextBox();
            this.dataGraph = new ZedGraph.ZedGraphControl();
            this.indicatorValues = new System.Windows.Forms.Label();
            this.statusStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // refreshTimer
            // 
            this.refreshTimer.Enabled = true;
            this.refreshTimer.Tick += new System.EventHandler(this.refreshTick);
            // 
            // toolStripStatusXY
            // 
            this.toolStripStatusXY.Name = "toolStripStatusXY";
            this.toolStripStatusXY.Size = new System.Drawing.Size(0, 17);
            // 
            // statusStrip1
            // 
            this.statusStrip1.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripStatusXY});
            this.statusStrip1.Location = new System.Drawing.Point(0, 427);
            this.statusStrip1.MinimumSize = new System.Drawing.Size(0, 25);
            this.statusStrip1.Name = "statusStrip1";
            this.statusStrip1.Size = new System.Drawing.Size(791, 25);
            this.statusStrip1.TabIndex = 1;
            this.statusStrip1.Text = "statusStrip1";
            // 
            // logTextBox
            // 
            this.logTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.logTextBox.Location = new System.Drawing.Point(10, 324);
            this.logTextBox.Multiline = true;
            this.logTextBox.Name = "logTextBox";
            this.logTextBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.logTextBox.Size = new System.Drawing.Size(772, 83);
            this.logTextBox.TabIndex = 5;
            this.logTextBox.Text = "Chart Log";
            this.logTextBox.Visible = false;
            // 
            // dataGraph
            // 
            this.dataGraph.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.dataGraph.Location = new System.Drawing.Point(10, 3);
            this.dataGraph.Name = "dataGraph";
            this.dataGraph.ScrollGrace = 0;
            this.dataGraph.ScrollMaxX = 0;
            this.dataGraph.ScrollMaxY = 0;
            this.dataGraph.ScrollMaxY2 = 0;
            this.dataGraph.ScrollMinX = 0;
            this.dataGraph.ScrollMinY = 0;
            this.dataGraph.ScrollMinY2 = 0;
            this.dataGraph.Size = new System.Drawing.Size(772, 404);
            this.dataGraph.TabIndex = 0;
            this.dataGraph.MouseMoveEvent += new ZedGraph.ZedGraphControl.ZedMouseEventHandler(this.DataGraphMouseMoveEvent);
		    this.dataGraph.MouseHover += new EventHandler(this.DataGraphMouseHoverEvent);
            this.dataGraph.ScrollEvent += new System.Windows.Forms.ScrollEventHandler(this.DataGraphScrollEvent);
            this.dataGraph.ContextMenuBuilder += new ZedGraph.ZedGraphControl.ContextMenuBuilderEventHandler(this.DataGraphContextMenuBuilder);
            // 
            // indicatorValues
            // 
            this.indicatorValues.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.indicatorValues.Location = new System.Drawing.Point(10, 410);
            this.indicatorValues.Name = "indicatorValues";
            this.indicatorValues.Size = new System.Drawing.Size(772, 17);
            this.indicatorValues.TabIndex = 8;
            // 
            // ChartControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.indicatorValues);
            this.Controls.Add(this.logTextBox);
            this.Controls.Add(this.statusStrip1);
            this.Controls.Add(this.dataGraph);
            this.Name = "ChartControl";
            this.Size = new System.Drawing.Size(791, 452);
            this.Load += new System.EventHandler(this.ChartLoad);
            this.Resize += new System.EventHandler(this.ChartResize);
            this.statusStrip1.ResumeLayout(false);
            this.statusStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private ZedGraphControl dataGraph;
        private System.Windows.Forms.Timer refreshTimer;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusXY;
        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.TextBox logTextBox;
        private System.Windows.Forms.Label indicatorValues;
		
		public ZedGraphControl DataGraph {
			get { return dataGraph; }
		}
	}
}
