using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Globalization;
using System.ComponentModel;

namespace Kaenx.Creator.Models
{
    public class MemorySection : INotifyPropertyChanged
    {   
        public int Address;

        public string Name
        {
            get { return $"0x{Address:X4}"; }
        }

        public MemorySection(int address)
        {
            Address = address;
        }

        public MemorySection(int address, int usedBits = 0)
        {
            Address = address;
        }

        
        public ObservableCollection<MemoryByte> Bytes {get;set;} = new ObservableCollection<MemoryByte>();

        private List<int> fillColor;
        public List<int> FillColor
        {
            get{
                CalculateFillColors();
                return fillColor;
            }
        }


        private void CalculateFillColors()
        {
            if(fillColor != null) return;
            fillColor = new List<int>();
            foreach(MemoryByte mbyte in Bytes)
            {

                switch(mbyte.Usage)
                {
                    case MemoryByteUsage.Used:
                    //TODO implement converter from int to brush
                        //fillColor.Add(new SolidColorBrush(Colors.Gray));
                        fillColor.Add(0);
                        continue;

                    case MemoryByteUsage.GroupAddress:
                        //fillColor.Add(new SolidColorBrush(Colors.Violet));
                        fillColor.Add(1);
                        continue;

                    case MemoryByteUsage.Association:
                        //fillColor.Add(new SolidColorBrush(Colors.Brown));
                        fillColor.Add(2);
                        continue;

                    case MemoryByteUsage.Coms:
                        //fillColor.Add(new SolidColorBrush(Colors.Chocolate));
                        fillColor.Add(3);
                        continue;
                    
                    case MemoryByteUsage.Module:
                        //fillColor.Add(new SolidColorBrush(Colors.Pink));
                        fillColor.Add(4);
                        continue;
                }
                
                if(mbyte.UnionList.Count > 0)
                {
                    fillColor.Add(mbyte.UnionList.Count == 1 ? 9 : 10); //Blue/MediumPurple
                    continue;
                }

                switch(mbyte.CheckFreeBits())
                {
                    case -1:
                        //fillColor.Add(new SolidColorBrush(Colors.Purple));
                        fillColor.Add(5);
                        break;

                    case 1:
                        //fillColor.Add(new SolidColorBrush(Colors.Red));
                        fillColor.Add(6);
                        break;

                    case 0:
                        //fillColor.Add(new SolidColorBrush(Colors.Green));
                        fillColor.Add(7);
                        break;

                    default:
                        //fillColor.Add(new SolidColorBrush(Colors.Orange));
                        fillColor.Add(8);
                        break;
                }
            }

            int toadd = 16-fillColor.Count;
            for(int i = 0; i<toadd; i++)
                fillColor.Add(0);
        }


        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}