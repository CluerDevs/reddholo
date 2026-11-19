using System;
using System.Collections.Generic;
using System.Diagnostics;
using BcuHolo.Plugins;
using LibreHardwareMonitor.Hardware;

namespace BcuHolo.Modules.Sensors
{
    /// <summary>
    /// LibreHardwareMonitor-backed CPU / motherboard / storage / memory sensors. Local only: no network code.
    /// CPU temps, power and VIDs need the PawnIO driver installed (LHM loads signed modules into it); without it
    /// those sensors are simply absent and Read() returns whatever else works.
    /// </summary>
    public sealed class LhmSensorSource : ISensorSource, IDisposable
    {
        /// <summary>Hardware names of storage devices, so the UI can tell drives from board temperatures.</summary>
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> StorageGroups = new();

        private Computer _computer;
        private bool _openFailed;

        public string Name => "LHM";

        public IEnumerable<SensorReading> Read()
        {
            var list = new List<SensorReading>();
            try
            {
                if (_openFailed) return list;
                if (_computer == null)
                {
                    var c = new Computer { IsCpuEnabled = true, IsMotherboardEnabled = true, IsStorageEnabled = true, IsMemoryEnabled = true, IsGpuEnabled = false };
                    c.Open();
                    _computer = c;
                }
                _computer.Accept(new UpdateVisitor());
                foreach (var hw in _computer.Hardware) Collect(hw, list);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("LhmSensorSource: " + ex.Message);
                if (_computer == null) _openFailed = true; // don't retry a failing Open() every second
            }
            return list;
        }

        private static void Collect(IHardware hw, List<SensorReading> into)
        {
            if (hw.HardwareType == HardwareType.Storage) StorageGroups[hw.Name] = 0;
            foreach (var s in hw.Sensors)
            {
                string unit = UnitOf(s.SensorType);
                if (unit == null || !s.Value.HasValue || float.IsNaN(s.Value.Value)) continue;
                into.Add(new SensorReading("LHM", hw.Name, s.Name, s.Value.Value, unit));
            }
            foreach (var sub in hw.SubHardware) Collect(sub, into);
        }

        private static string UnitOf(SensorType t)
        {
            switch (t)
            {
                case SensorType.Temperature: return "°C";
                case SensorType.Voltage: return "V";
                case SensorType.Power: return "W";
                case SensorType.Load: return "%";
                case SensorType.Fan: return "RPM";
                case SensorType.Clock: return "MHz";
                default: return null;
            }
        }

        public void Dispose()
        {
            try { _computer?.Close(); } catch (Exception ex) { Trace.WriteLine("LhmSensorSource close: " + ex.Message); }
            _computer = null;
        }

        private sealed class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer) => computer.Traverse(this);
            public void VisitHardware(IHardware hardware)
            {
                hardware.Update();
                foreach (var sub in hardware.SubHardware) sub.Accept(this);
            }
            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }
    }
}
