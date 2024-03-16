using Kaenx.Creator;
using Kaenx.Creator.Models;

namespace Kaenx.Creator.Classes
{
    public class MemoryHelper
    {
        public static void MemoryCalculation(AppVersion ver, Memory mem)
        {
            mem.Sections.Clear();

            if(mem.Type == MemoryTypes.Absolute)
                mem.StartAddress = mem.Address - (mem.Address % 16);
            else
            {
                mem.StartAddress = 0;
                mem.Address = 0;
            }

            foreach(Module mod in ver.Modules)
                ClearModuleMemory(mod);

            if(!mem.IsAutoSize)
                mem.AddBytes(mem.Size);

            if(mem.Type == MemoryTypes.Absolute)
            {
                if(ver.AddressMemoryObject == mem)
                    MemoryCalculationGroups(ver, mem);
                if(ver.AssociationMemoryObject == mem)
                    MemoryCalculationAssocs(ver, mem);
                if(ver.ComObjectMemoryObject == mem)
                    MemoryCalculationComs(ver, mem);
            }
            MemoryCalculationRegular(ver, mem);
        }

        private static void ClearModuleMemory(Module mod)
        {
            mod.Memory.Sections.Clear();
            foreach(Module xmod in mod.Modules)
                ClearModuleMemory(xmod);
        }

        private static void MemoryCalculationGroups(AppVersion ver, Memory mem)
        {
            int maxSize = (ver.AddressTableMaxCount+2) * 2;
            maxSize--; //TODO check why the heck it is smaller
            if(mem.IsAutoSize && (maxSize + ver.AddressTableOffset) > mem.GetCount())
                mem.AddBytes((maxSize + ver.AddressTableOffset) - mem.GetCount());
            //if(mem.Size < maxSize) maxSize = mem.Size;
            mem.SetBytesUsed(MemoryByteUsage.GroupAddress, maxSize, ver.AddressTableOffset);
        }

        private static void MemoryCalculationAssocs(AppVersion ver, Memory mem)
        {
            int maxSize = (ver.AssociationTableMaxCount+1) * 2;
            maxSize--;
            if(mem.IsAutoSize && (maxSize + ver.AssociationTableOffset) > mem.GetCount())
                mem.AddBytes((maxSize + ver.AssociationTableOffset) - mem.GetCount());
            //if(mem.Size < maxSize) maxSize = mem.Size;
            mem.SetBytesUsed(MemoryByteUsage.Association, maxSize, ver.AssociationTableOffset);
        }

        private static void MemoryCalculationComs(AppVersion ver, Memory mem)
        {

            int maxSize = (ver.ComObjects.Count * 3) + 2;
            if(mem.IsAutoSize && (maxSize + ver.ComObjectTableOffset) > mem.GetCount())
                mem.AddBytes((maxSize + ver.ComObjectTableOffset) - mem.GetCount());
            //if(mem.Size < maxSize) maxSize = mem.Size;
            mem.SetBytesUsed(MemoryByteUsage.Coms, maxSize, ver.ComObjectTableOffset);
        }

        private static void MemCalcStatics(IVersionBase vbase, Memory mem, int memId)
        {
            List<Parameter> paras = vbase.Parameters.Where(p => p.MemoryId == memId && p.IsInUnion == false && p.SavePath != SavePaths.Nowhere).ToList();

            foreach(Parameter para in paras.Where(p => p.Offset != -1))
            {
                int sizeInByte = (int)Math.Ceiling(para.ParameterTypeObject.SizeInBit / 8.0);
                if((para.Offset + sizeInByte) > mem.GetCount())
                {
                    if(!mem.IsAutoSize) throw new Exception($"Parameter {para.Name} does not fit in Memory"); //TODO MessageBox.Show(string.Format(Properties.Messages.memcalc_para, para.Name), Properties.Messages.memcalc_title, MessageBoxButton.OK, MessageBoxImage.Error);
                    
                    int toadd = ((para.Offset + sizeInByte) - mem.GetCount());
                    //if(para.ParameterTypeObject.SizeInBit > 8) toadd += (para.ParameterTypeObject.SizeInBit / 8) - 1;
                    mem.AddBytes(toadd);
                }

                mem.SetBytesUsed(para);
            }

            foreach (Union union in vbase.Unions.Where(u => u.MemoryId == mem.UId && u.Offset != -1 && u.SavePath != SavePaths.Nowhere))
            {
                int sizeInByte = (int)Math.Ceiling(union.SizeInBit / 8.0);
                if(union.Offset + sizeInByte > mem.GetCount())
                {
                    if(!mem.IsAutoSize) throw new Exception($"Union {union.Name} does not fit in Memory"); //TODO MessageBox.Show(string.Format(Properties.Messages.memcalc_union, union.Name), Properties.Messages.memcalc_title, MessageBoxButton.OK, MessageBoxImage.Error);

                    int toadd = 1;
                    if(union.SizeInBit > 8) toadd = (union.Offset - mem.GetCount()) + (union.SizeInBit / 8);
                    mem.AddBytes(toadd);
                }

                mem.SetBytesUsed(union, vbase.Parameters.Where(p => p.UnionId == union.UId).ToList());
            }
        }

        private static void MemCalcAuto(IVersionBase vbase, Memory mem, int memId)
        {
            List<Parameter> paras = vbase.Parameters.Where(p => p.MemoryId == memId && p.IsInUnion == false && p.SavePath != SavePaths.Nowhere).ToList();
            IEnumerable<Parameter> list1;
            if(mem.IsAutoOrder) list1 = paras.ToList();
            else list1 = paras.Where(p => p.Offset == -1);
            foreach(Parameter para in list1)
            {
                (int offset, int offsetbit) result = mem.GetFreeOffset(para.ParameterTypeObject.SizeInBit);
                para.Offset = result.offset;
                para.OffsetBit = result.offsetbit;
                mem.SetBytesUsed(para);
            }

            IEnumerable<Union> list2;
            if(mem.IsAutoOrder) list2 = vbase.Unions.Where(u => u.MemoryId == memId && u.SavePath != SavePaths.Nowhere);
            else list2 = vbase.Unions.Where(u => u.MemoryId == memId && u.Offset == -1);
            foreach (Union union in list2)
            {
                (int offset, int offsetbit) result = mem.GetFreeOffset(union.SizeInBit);
                union.Offset = result.offset;
                union.OffsetBit = result.offsetbit;
                mem.SetBytesUsed(union, vbase.Parameters.Where(p => p.UnionId == union.UId).ToList());
            }
        }

        private static void MemoryCalculationRegular(AppVersion ver, Memory mem)
        {
            if(!mem.IsAutoPara || (mem.IsAutoPara && !mem.IsAutoOrder))
            {
                foreach(Module mod in ver.Modules)
                    MemCalcStatics(mod, mod.Memory, mem.UId);
                    
                MemCalcStatics(ver, mem, mem.UId);
            }

            if(mem.IsAutoPara)
            {
                foreach(Module mod in ver.Modules)
                    MemCalcAuto(mod, mod.Memory, mem.UId);

                MemCalcAuto(ver, mem, mem.UId);
            }

            List<Models.Dynamic.DynModule> mods = new List<Models.Dynamic.DynModule>();
            Helper.GetModules(ver.Dynamics[0], mods);
            int highestComNumber = ver.ComObjects.OrderByDescending(c => c.Number).FirstOrDefault()?.Number ?? 0;
            int offset = mem.GetFreeOffset();
            bool firstComAPP = true;

            List<string> checkedMods = new List<string>();

            foreach(Models.Dynamic.DynModule dmod in mods)
            {
                Models.Dynamic.DynModuleArg argParas = dmod.Arguments.SingleOrDefault(a => a.ArgumentId == dmod.ModuleObject.ParameterBaseOffsetUId);
                if(argParas == null) continue;

                if(!mem.IsAutoPara || (mem.IsAutoPara && !mem.IsAutoOrder && !string.IsNullOrEmpty(argParas.Value)))
                {
                    int modSize = dmod.ModuleObject.Memory.GetCount();
                    int start = int.Parse(argParas.Value);
                    mem.SetBytesUsed(MemoryByteUsage.Module, modSize, start, dmod?.ModuleObject);
                }

                if(mem.IsAutoPara && (string.IsNullOrEmpty(argParas.Value) || mem.IsAutoOrder))
                {
                    int modSize = dmod.ModuleObject.Memory.GetCount();
                    mem.AddBytes(modSize);
                    argParas.Value = offset.ToString();
                    argParas.Argument.Allocates = modSize;
                    mem.SetBytesUsed(MemoryByteUsage.Module, modSize, offset, dmod?.ModuleObject);

                    if(argParas.UseAllocator && !checkedMods.Contains(dmod.ModuleObject.Name))
                    {
                        argParas.Allocator.Start = offset;
                        int modCounter = mods.Count(m => m.ModuleUId == dmod.ModuleUId);
                        argParas.Allocator.Max = argParas.Allocator.Start + (modCounter * modSize);
                    }

                    offset += modSize;
                }

                if(dmod.ModuleObject.IsComObjectBaseNumberAuto)
                {
                    Models.Dynamic.DynModuleArg argComs = dmod.Arguments.SingleOrDefault(a => a.ArgumentId == dmod.ModuleObject.ComObjectBaseNumberUId);
                    if(argComs != null)
                    {
                        int highestComNumber2 = dmod.ModuleObject.ComObjects.OrderByDescending(c => c.Number).FirstOrDefault()?.Number ?? 0;
                        int lowestComNumber2 = dmod.ModuleObject.ComObjects.OrderBy(c => c.Number).FirstOrDefault()?.Number ?? 1;
                        
                        if(highestComNumber == 0 && lowestComNumber2 == 0 || firstComAPP)
                        {
                            highestComNumber++;
                            firstComAPP = false;
                        }
                        argComs.Value = highestComNumber.ToString();

                        if(argComs.UseAllocator && !checkedMods.Contains(dmod.ModuleObject.Name))
                        {
                            argComs.Allocator.Start = (long)highestComNumber;
                            argComs.Allocator.Max = argComs.Allocator.Start + (mods.Count(m => m.ModuleUId == dmod.ModuleUId) * (highestComNumber2+1));
                        }

                        argComs.Argument.Allocates = highestComNumber2 + 1;
                        highestComNumber += argComs.Argument.Allocates;
                    }
                }

                if(!checkedMods.Contains(dmod.ModuleObject.Name))
                    checkedMods.Add(dmod.ModuleObject.Name);
            }

            ver.HighestComNumber = highestComNumber;

            if (mem.IsAutoSize)
                mem.Size = mem.GetCount();
        }
    }
}