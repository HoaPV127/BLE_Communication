using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth;
using Windows.Devices;
using Windows.Foundation;
using Windows;
using Windows.Storage.Streams;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using System.Threading;
using Windows.UI.Core;
using Windows.ApplicationModel.Core;

namespace BLE_Communication
{
    public partial class Form1 : Form
    {

        //private BluetoothLEAdvertisementPublisher m_publisher;
        private BluetoothLEAdvertisementWatcher m_watcher;
        //private Dictionary<ulong, BluetoothLEDevice> m_devices;
        private List<ulong> m_devices;
        private BluetoothLEDevice currDevice;

        public Form1()
        {
            
            InitializeComponent();

            //m_devices = new Dictionary<ulong, BluetoothLEDevice>();    
            m_devices = new List<ulong>();
            m_watcher = new BluetoothLEAdvertisementWatcher();

            Console.WriteLine("Start Scan");
            //m_watcher.AdvertisementFilter = 
            m_watcher.ScanningMode = BluetoothLEScanningMode.Active;
            m_watcher.Received += OnAdvertisementReceived;
            m_watcher.Start();

        }
        private async void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementReceivedEventArgs eventArgs)
        {
            string devName = null;
            //AddScanedDevice(eventArgs.BluetoothAddress);
            var manu = eventArgs.Advertisement.ManufacturerData[0].CompanyId;
            ulong devAddr = eventArgs.BluetoothAddress;
            
            //BluetoothLEDevice scanDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(devAddr);
            //devName = scanDevice.Name;

            //if (m_devices.ContainsKey(eventArgs.BluetoothAddress) == false)
            if (m_devices.Contains(devAddr) == false)
            {
                //m_devices.Add(eventArgs.BluetoothAddress, device);
                m_devices.Add(devAddr);

                Button dev = new Button();

                if (string.IsNullOrEmpty(devName))
                    dev.Text = "Unknown";
                else
                    dev.Text = devName;
                dev.Width = 150;
                dev.Height = 30;
                dev.Location = new System.Drawing.Point(this.Width - 190, 30 * m_devices.Count);
                dev.Click += (sender, EventArgs) => { SelectAndConnect(sender, EventArgs, devAddr); };

                if (this.InvokeRequired)
                {
                    Invoke(new Action(() =>
                    {
                        this.Controls.Add(dev);
                    }));
                }
                else
                {
                    this.Controls.Add(dev);
                }

            }
        }

        private async void SelectAndConnect(object sender, EventArgs e, ulong addr)
        {
            //BluetoothLEDevice device = m_devices[addr];
            //currDevice = device;
            currDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(addr);
            BluetoothLEDevice device = currDevice;

            if (device.ConnectionStatus == BluetoothConnectionStatus.Connected)
            {
                return;
            }

            DeviceAccessStatus status = await device.RequestAccessAsync();

            if (status == DeviceAccessStatus.Allowed)
            {
                if (this.msgRTbx.InvokeRequired)
                {
                    Invoke(new Action(() =>
                    {
                        this.msgRTbx.AppendText($"Connected to {device.Name}\n");
                    }));
                }
                else
                {
                    this.msgRTbx.AppendText($"Connected to {device.Name}\n");
                }
            }

            GattDeviceServicesResult devService = await device.GetGattServicesAsync();

            if (devService.Status == GattCommunicationStatus.Success)
            {
                var services = devService.Services;

                foreach (var service in services)
                {
                    if (!service.Uuid.ToString().Contains("0000ffe0-"))
                    {
                        continue;
                    }
                    GattCharacteristicsResult devCharacterist = await service.GetCharacteristicsAsync();

                    if (devCharacterist.Status == GattCommunicationStatus.Success)
                    {
                        foreach (var characteristic in devCharacterist.Characteristics)
                        {
                            GattCharacteristicProperties devProperties = characteristic.CharacteristicProperties;

                            if (devProperties.HasFlag(GattCharacteristicProperties.Notify))
                            {
                                GattCommunicationStatus commStatus = GattCommunicationStatus.AccessDenied;

                                try
                                {
                                    commStatus = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                                                GattClientCharacteristicConfigurationDescriptorValue.Notify);
                                }
                                catch (Exception ex)
                                {
                                    if (this.msgRTbx.InvokeRequired)
                                    {
                                        Invoke(new Action(() =>
                                        {
                                            this.msgRTbx.AppendText(ex.Message);
                                        }));
                                    }
                                    else
                                    {
                                        this.msgRTbx.AppendText(ex.Message);
                                    }
                                }
                                if (commStatus == GattCommunicationStatus.Success)
                                {
                                    characteristic.ValueChanged += Characteristic_ValueChanged;
                                    // Server has been informed of clients interest.
                                }
                            }
                        }
                    }
                }
            }
        }

        void Characteristic_ValueChanged(GattCharacteristic eventSender, GattValueChangedEventArgs args)
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);

            var value = reader.ReadString(args.CharacteristicValue.Length);

            if (this.msgRTbx.InvokeRequired)
            {
                Invoke(new Action(() =>
                {
                    this.msgRTbx.AppendText($"{currDevice.Name}: {value}\n");
                }));
            }
            else
            {
                this.msgRTbx.AppendText($"{currDevice.Name}: {value}\n");
            }
        }

        private async void sendBtn_Click(object sender, EventArgs e)
        {
            string sendText = this.sendTbx.Text.ToString();

            var writer = new DataWriter();
            // WriteByte used for simplicity. Other common functions - WriteInt16 and WriteSingle
            writer.WriteString(sendText);
            GattCommunicationStatus result = GattCommunicationStatus.Unreachable;

            GattDeviceServicesResult devService = await currDevice.GetGattServicesAsync();

            if (devService.Status == GattCommunicationStatus.Success)
            {


                var services = devService.Services;

                foreach (var service in services)
                {
                    if (!service.Uuid.ToString().Contains("0000ffe0-"))
                    {
                        continue;
                    }
                    GattCharacteristicsResult devCharacterist = await service.GetCharacteristicsAsync();

                    if (devCharacterist.Status == GattCommunicationStatus.Success)
                    {
                        foreach (var characteristic in devCharacterist.Characteristics)
                        {
                            GattCharacteristicProperties devProperties = characteristic.CharacteristicProperties;

                            if (devProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
                            {
                                try
                                {
                                    result = await characteristic.WriteValueAsync(writer.DetachBuffer());
                                }
                                catch (Exception ex)
                                {
                                    if (this.msgRTbx.InvokeRequired)
                                    {
                                        Invoke(new Action(() =>
                                        {
                                            this.msgRTbx.AppendText($"{ex.Message}\n");
                                        }));
                                    }
                                    else
                                    {
                                        this.msgRTbx.AppendText($"{ex.Message}\n");
                                    }
                                }

                                if (result == GattCommunicationStatus.Success)
                                {
                                    if (this.msgRTbx.InvokeRequired)
                                    {
                                        Invoke(new Action(() =>
                                        {
                                            this.msgRTbx.AppendText($"Send to {currDevice.Name}: {sendText}\n");
                                        }));
                                    }
                                    else
                                    {
                                        this.msgRTbx.AppendText($"Send to {currDevice.Name}: {sendText}\n");
                                    }
                                }

                            }
                        }
                    }
                }
            }
        }

    }
}
