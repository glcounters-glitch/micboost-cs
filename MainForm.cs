using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using NAudio.Wave;

namespace MicBoost
{
    public class MainForm : Form
    {
        // ── Audio ────────────────────────────────────────────────────
        private WaveInEvent?  _waveIn;
        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _buffer;
        private bool _running = false;
        private float _gain   = 3.0f;
        private float _level  = 0f;
        private bool  _clip   = false;

        // ── Controls ─────────────────────────────────────────────────
        private ComboBox cmbInput  = new();
        private ComboBox cmbOutput = new();
        private TrackBar trkGain   = new();
        private Label    lblGain   = new();
        private Button   btnStart  = new();
        private Panel    pnlVU     = new();
        private Label    lblClip   = new();
        private NotifyIcon? _trayIcon;
        private System.Windows.Forms.Timer _vuTimer = new();

        // ── Colors ───────────────────────────────────────────────────
        static readonly Color BG      = Color.FromArgb(15,  15,  19);
        static readonly Color PANEL   = Color.FromArgb(26,  26,  36);
        static readonly Color ACCENT  = Color.FromArgb(0,   229, 255);
        static readonly Color ACCENT2 = Color.FromArgb(255, 60,  110);
        static readonly Color TEXT    = Color.FromArgb(232, 234, 240);
        static readonly Color MUTED   = Color.FromArgb(85,  85,  112);
        static readonly Color GREEN   = Color.FromArgb(0,   230, 118);
        static readonly Color YELLOW  = Color.FromArgb(255, 234, 0);
        static readonly Color RED     = Color.FromArgb(255, 23,  68);

        public MainForm()
        {
            InitializeComponent();
            LoadDevices();
            SetupTray();
            _vuTimer.Interval = 40;
            _vuTimer.Tick += (s, e) => pnlVU.Invalidate();
            _vuTimer.Start();
        }

        // ─────────────────────────────────────────────────────────────
        //  UI
        // ─────────────────────────────────────────────────────────────
        private void InitializeComponent()
        {
            Text            = "MicBoost";
            Size            = new Size(440, 540);
            MinimumSize     = new Size(440, 540);
            MaximumSize     = new Size(440, 540);
            BackColor       = BG;
            ForeColor       = TEXT;
            Font            = new Font("Consolas", 9f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox     = false;
            StartPosition   = FormStartPosition.CenterScreen;

            // ── Title ──
            var lblTitle = new Label
            {
                Text      = "◈ MicBoost",
                Font      = new Font("Consolas", 18f, FontStyle.Bold),
                ForeColor = ACCENT,
                BackColor = BG,
                Location  = new Point(18, 18),
                AutoSize  = true
            };

            var lblDot = new Label
            {
                Text      = "●",
                Font      = new Font("Consolas", 16f),
                ForeColor = MUTED,
                BackColor = BG,
                Location  = new Point(390, 22),
                AutoSize  = true,
                Name      = "lblDot"
            };

            var sep = new Panel
            {
                Location  = new Point(18, 58),
                Size      = new Size(390, 1),
                BackColor = MUTED
            };

            // ── Device panel ──
            var pnlDev = new Panel
            {
                Location  = new Point(18, 68),
                Size      = new Size(390, 145),
                BackColor = PANEL
            };

            var lblIn = MakeLabel("INPUT  (USB Microphone)", 12, 10, MUTED);
            cmbInput = MakeCombo(12, 28);

            var lblOut = MakeLabel("OUTPUT  (VB-CABLE / Virtual device)", 12, 72, MUTED);
            cmbOutput = MakeCombo(12, 90);

            var btnRefresh = new Button
            {
                Text      = "↻ Refresh",
                Font      = new Font("Consolas", 8f),
                ForeColor = MUTED,
                BackColor = PANEL,
                FlatStyle = FlatStyle.Flat,
                Location  = new Point(300, 120),
                Size      = new Size(78, 22),
                Cursor    = Cursors.Hand
            };
            btnRefresh.FlatAppearance.BorderSize = 0;
            btnRefresh.Click += (s, e) => LoadDevices();

            pnlDev.Controls.AddRange(new Control[]
                { lblIn, cmbInput, lblOut, cmbOutput, btnRefresh });

            // ── Gain ──
            var lblGainHdr = MakeLabel("GAIN", 18, 230, MUTED);
            lblGainHdr.Location = new Point(18, 230);

            trkGain = new TrackBar
            {
                Minimum  = 10,
                Maximum  = 100,
                Value    = 30,
                TickFrequency = 10,
                Location = new Point(18, 248),
                Size     = new Size(260, 36),
                BackColor = BG
            };
            trkGain.Scroll += OnGainScroll;

            lblGain = new Label
            {
                Text      = "×3.0  (+9.5 dB)",
                Font      = new Font("Consolas", 12f, FontStyle.Bold),
                ForeColor = ACCENT,
                BackColor = BG,
                Location  = new Point(282, 252),
                Size      = new Size(148, 28),
                TextAlign = ContentAlignment.MiddleLeft
            };

            // ── VU meter ──
            var lblLevel = MakeLabel("LEVEL", 18, 300, MUTED);

            pnlVU = new Panel
            {
                Location  = new Point(18, 318),
                Size      = new Size(390, 22),
                BackColor = PANEL,
                Cursor    = Cursors.Default
            };
            pnlVU.Paint += OnVUPaint;

            lblClip = new Label
            {
                Text      = "",
                ForeColor = RED,
                BackColor = BG,
                Location  = new Point(360, 342),
                Size      = new Size(48, 16),
                TextAlign = ContentAlignment.MiddleRight
            };

            // ── Start/Stop button ──
            btnStart = new Button
            {
                Text      = "▶  START",
                Font      = new Font("Consolas", 11f, FontStyle.Bold),
                ForeColor = BG,
                BackColor = ACCENT,
                FlatStyle = FlatStyle.Flat,
                Size      = new Size(160, 44),
                Cursor    = Cursors.Hand
            };
            btnStart.FlatAppearance.BorderSize = 0;
            btnStart.Location = new Point((440 - 160) / 2, 378);
            btnStart.Click += OnToggle;

            // ── Footer ──
            var lblFooter1 = MakeLabel("Route: USB Mic → MicBoost → VB-CABLE → Apps", 18, 440, MUTED);
            var lblFooter2 = MakeLabel("In apps: select  CABLE Output  as microphone",  18, 458, MUTED);

            Controls.AddRange(new Control[]
            {
                lblTitle, lblDot, sep, pnlDev,
                lblGainHdr, trkGain, lblGain,
                lblLevel, pnlVU, lblClip,
                btnStart, lblFooter1, lblFooter2
            });
        }

        private Label MakeLabel(string text, int x, int y, Color color)
            => new Label
            {
                Text      = text,
                ForeColor = color,
                BackColor = Color.Transparent,
                Location  = new Point(x, y),
                AutoSize  = true
            };

        private ComboBox MakeCombo(int x, int y)
        {
            var cb = new ComboBox
            {
                Location      = new Point(x, y),
                Size          = new Size(366, 22),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor     = BG,
                ForeColor     = TEXT,
                FlatStyle     = FlatStyle.Flat,
                Font          = new Font("Consolas", 8.5f)
            };
            return cb;
        }

        // ─────────────────────────────────────────────────────────────
        //  Devices
        // ─────────────────────────────────────────────────────────────
        private void LoadDevices()
        {
            cmbInput.Items.Clear();
            cmbOutput.Items.Clear();

            var seenIn  = new HashSet<string>();
            var seenOut = new HashSet<string>();

            // Inputs
            int inCount = WaveIn.DeviceCount;
            for (int i = 0; i < inCount; i++)
            {
                var cap  = WaveIn.GetCapabilities(i);
                var name = cap.ProductName;
                if (seenIn.Contains(name)) continue;
                seenIn.Add(name);
                cmbInput.Items.Add(new DeviceItem(i, name));
            }

            // Outputs
            int outCount = WaveOut.DeviceCount;
            for (int i = 0; i < outCount; i++)
            {
                var cap  = WaveOut.GetCapabilities(i);
                var name = cap.ProductName;
                if (seenOut.Contains(name)) continue;
                seenOut.Add(name);
                cmbOutput.Items.Add(new DeviceItem(i, name));
            }

            // Auto-select: USB mic
            for (int i = 0; i < cmbInput.Items.Count; i++)
            {
                var n = cmbInput.Items[i]!.ToString()!.ToLower();
                if (n.Contains("usb") || n.Contains("pnp"))
                { cmbInput.SelectedIndex = i; break; }
            }
            if (cmbInput.SelectedIndex < 0 && cmbInput.Items.Count > 0)
                cmbInput.SelectedIndex = 0;

            // Auto-select: CABLE Input
            for (int i = 0; i < cmbOutput.Items.Count; i++)
            {
                var n = cmbOutput.Items[i]!.ToString()!.ToLower();
                if (n.Contains("cable input"))
                { cmbOutput.SelectedIndex = i; break; }
            }
            if (cmbOutput.SelectedIndex < 0 && cmbOutput.Items.Count > 0)
                cmbOutput.SelectedIndex = 0;
        }

        // ─────────────────────────────────────────────────────────────
        //  Audio engine
        // ─────────────────────────────────────────────────────────────
        private void OnToggle(object? s, EventArgs e)
        {
            if (!_running) StartAudio();
            else           StopAudio();
        }

        private void StartAudio()
        {
            if (cmbInput.SelectedItem is not DeviceItem inDev ||
                cmbOutput.SelectedItem is not DeviceItem outDev)
            {
                MessageBox.Show("Выбери входное и выходное устройство.", "MicBoost");
                return;
            }

            var fmt = new WaveFormat(44100, 16, 1);

            _waveIn = new WaveInEvent
            {
                DeviceNumber = inDev.Index,
                WaveFormat   = fmt,
                BufferMilliseconds = 20
            };

            _buffer  = new BufferedWaveProvider(fmt)
            {
                BufferDuration      = TimeSpan.FromMilliseconds(200),
                DiscardOnBufferOverflow = true
            };

            _waveIn.DataAvailable += OnData;

            _waveOut = new WaveOutEvent
            {
                DeviceNumber = outDev.Index,
                DesiredLatency = 60
            };
            _waveOut.Init(_buffer);

            try
            {
                _waveIn.StartRecording();
                _waveOut.Play();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска:\n{ex.Message}", "MicBoost");
                StopAudio();
                return;
            }

            _running = true;
            btnStart.Text      = "■  STOP";
            btnStart.BackColor = ACCENT2;
            btnStart.ForeColor = Color.White;
            cmbInput.Enabled   = false;
            cmbOutput.Enabled  = false;
            SetDot(GREEN);
        }

        private unsafe void OnData(object? s, WaveInEventArgs e)
        {
            var buf = e.Buffer;
            int bytes = e.BytesRecorded;

            // 16-bit PCM gain + RMS
            float sumSq = 0;
            bool  clip  = false;

            fixed (byte* p = buf)
            {
                short* samples = (short*)p;
                int count = bytes / 2;
                for (int i = 0; i < count; i++)
                {
                    float f = samples[i] / 32768f * _gain;
                    if (f >  1f) { f =  1f; clip = true; }
                    if (f < -1f) { f = -1f; clip = true; }
                    sumSq += f * f;
                    samples[i] = (short)(f * 32767f);
                }
                _level = MathF.Min(MathF.Sqrt(sumSq / (bytes / 2)) * 4f, 1f);
                _clip  = clip;
            }

            _buffer?.AddSamples(buf, 0, bytes);

            // update clip label on UI thread
            BeginInvoke(() => lblClip.Text = _clip ? "⚠ CLIP" : "");
        }

        private void StopAudio()
        {
            _waveIn?.StopRecording();
            _waveIn?.Dispose();
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _waveIn  = null;
            _waveOut = null;
            _buffer  = null;
            _level   = 0;
            _clip    = false;
            _running = false;

            btnStart.Text      = "▶  START";
            btnStart.BackColor = ACCENT;
            btnStart.ForeColor = BG;
            cmbInput.Enabled   = true;
            cmbOutput.Enabled  = true;
            lblClip.Text       = "";
            SetDot(MUTED);
        }

        // ─────────────────────────────────────────────────────────────
        //  Gain
        // ─────────────────────────────────────────────────────────────
        private void OnGainScroll(object? s, EventArgs e)
        {
            _gain = trkGain.Value / 10f;
            double db = 20 * Math.Log10(_gain);
            lblGain.Text = $"×{_gain:F1}  ({db:+0.0;-0.0} dB)";
        }

        // ─────────────────────────────────────────────────────────────
        //  VU meter
        // ─────────────────────────────────────────────────────────────
        private void OnVUPaint(object? s, PaintEventArgs e)
        {
            var g   = e.Graphics;
            int w   = pnlVU.Width;
            int h   = pnlVU.Height;
            int fill = (int)(w * _level);

            Color barColor = _level > 0.85f ? RED :
                             _level > 0.60f ? YELLOW : GREEN;

            g.Clear(PANEL);
            if (fill > 0)
                using (var br = new SolidBrush(barColor))
                    g.FillRectangle(br, 0, 0, fill, h);
        }

        // ─────────────────────────────────────────────────────────────
        //  Tray
        // ─────────────────────────────────────────────────────────────
        private void SetupTray()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using var br = new SolidBrush(ACCENT);
                g.FillEllipse(br, 1, 1, 14, 14);
                using var br2 = new SolidBrush(BG);
                g.FillRectangle(br2, 6, 3, 4, 7);
                g.FillEllipse(br2, 4, 8, 8, 4);
                g.FillRectangle(br2, 7, 12, 2, 3);
            }

            _trayIcon = new NotifyIcon
            {
                Icon    = Icon.FromHandle(bmp.GetHicon()),
                Text    = "MicBoost",
                Visible = true
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Показать", null, (s, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
            menu.Items.Add("Выход",    null, (s, e) => { StopAudio(); _trayIcon.Visible = false; Application.Exit(); });
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick     += (s, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        }

        private void SetDot(Color c)
        {
            if (Controls["lblDot"] is Label dot)
                dot.ForeColor = c;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized)
                Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            StopAudio();
            if (_trayIcon != null) _trayIcon.Visible = false;
            base.OnFormClosing(e);
        }
    }

    // ── Helper ───────────────────────────────────────────────────────
    public class DeviceItem
    {
        public int    Index { get; }
        public string Name  { get; }
        public DeviceItem(int index, string name) { Index = index; Name = name; }
        public override string ToString() => $"[{Index}] {Name}";
    }
}
