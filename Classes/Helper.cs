using Kaenx.Creator.Models;
using Kaenx.Creator.Models.Dynamic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenKNX.Toolbox.Sign;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Xml.Linq;

namespace Kaenx.Creator.Classes
{
    public class Helper 
    {
        public static int CurrentVersion { get; set; } = 10;
        public static ObservableCollection<Models.DataPointType> DPTs { get; set; }
        public static ObservableCollection<Models.MaskVersion> BCUs { get; set; }
        private static Dictionary<long, Parameter> Paras;
        private static Dictionary<long, ParameterRef> ParaRefs;
        private static Dictionary<long, ComObject> Coms;
        private static Dictionary<long, ComObjectRef> ComRefs;

        public static bool CheckExportNamespace(int ns)
        {
            return !string.IsNullOrEmpty(SignHelper.FindEtsPath(ns));
        }

        public static string CheckImportVersion(string json, int version)
        {
            if(version < 7)
            {
                json = json.Replace("\\\"IsLocal\\\":true", "\\\"IsGlobal\\\":false");
                json = json.Replace("\\\"IsLocal\\\":false", "\\\"IsGlobal\\\":true");
            }

            JObject gen = JObject.Parse(json);

            if(version < 1)
            {
                foreach(JObject app in gen["Applications"])
                {
                    foreach(JObject ver in app["Versions"])
                    {
                        ObservableCollection<Language> langs = ver["Languages"].ToObject<ObservableCollection<Language>>();

                        for(int i = 0; i < ver["Parameters"].Count(); i++)
                        {
                            string oldSuffix = "";
                            List<Translation> trans = new List<Translation>();
                            if(!string.IsNullOrEmpty(ver["Parameters"][i]["Suffix"].ToString()))
                            {
                                oldSuffix = ver["Parameters"][i]["Suffix"].ToString();
                            }
                            foreach(Language lang in langs)
                                trans.Add(new Translation(lang, oldSuffix));
                            ver["Parameters"][i]["Suffix"] = JValue.FromObject(trans);
                        }
                        
                        for(int i = 0; i < ver["ParameterRefs"].Count(); i++)
                        {
                            Parameter para = ver["ParameterRefs"][i].ToObject<Parameter>();
                            para.Suffix = new ObservableCollection<Translation>();
                            foreach(Language lang in langs)
                                para.Suffix.Add(new Translation(lang, ver["ParameterRefs"][i]["Suffix"].Value<string>()));
                            ver["ParameterRefs"][i] = JObject.FromObject(para);
                        }

                        foreach(JObject jmodule in ver["Modules"])
                        {
                            for(int i = 0; i < jmodule["Parameters"].Count(); i++)
                            {
                                Parameter para = jmodule["Parameters"][i].ToObject<Parameter>();
                                para.Suffix = new ObservableCollection<Translation>();
                                foreach(Language lang in langs)
                                    para.Suffix.Add(new Translation(lang, jmodule["Parameters"][i]["Suffix"].Value<string>()));
                                jmodule["Parameters"][i] = JObject.FromObject(para);
                            }
                            
                            for(int i = 0; i < jmodule["ParameterRefs"].Count(); i++)
                            {
                                Parameter para = jmodule["ParameterRefs"][i].ToObject<Parameter>();
                                para.Suffix = new ObservableCollection<Translation>();
                                foreach(Language lang in langs)
                                    para.Suffix.Add(new Translation(lang, jmodule["ParameterRefs"][i]["Suffix"].Value<string>()));
                                jmodule["ParameterRefs"][i] = JObject.FromObject(para);
                            }
                        }
                    }
                }
            }
            
            if(version < 2)
            {
                foreach(JObject app in gen["Applications"])
                {
                    List<AppVersionModel> newVers = new List<AppVersionModel>();
                    foreach(JObject ver in app["Versions"])
                    {
                        AppVersionModel model = new AppVersionModel();
                        model.Version = ver.ToString();
                        model.Number = (int)ver["Number"];
                        model.Name = ver["Name"].ToString();
                        newVers.Add(model);
                    }
                    app["Versions"] = JValue.FromObject(newVers);
                }
            }

            if(version < 3)
            {   
                Dictionary<int, int> newParaTypeList = new Dictionary<int, int>() 
                {
                    { 0, 12 },
                    { 1, 2 },
                    { 2, 8 },
                    { 3, 9 },
                    { 4, 3 },
                    { 5, 4 },
                    { 6, 5 },
                    { 7, 10 },
                    { 8, 7 },
                    { 9, 6 },
                    { 10, 0 }
                }; 
                Dictionary<int, int> newAccessList = new Dictionary<int, int>()
                {
                    { 0, 2 },
                    { 1, 0 },
                    { 2, 1 },
                    { 3, 2 }
                };

                foreach(JObject app in gen["Applications"])
                {
                    List<AppVersionModel> newVers = new List<AppVersionModel>();
                    foreach(JObject ver in app["Versions"])
                    {
                        JObject jver = JObject.Parse(ver["Version"].ToString());
                        foreach(JObject ptype in jver["ParameterTypes"])
                        {
                            ptype["Type"] = newParaTypeList[int.Parse(ptype["Type"].ToString())];
                        }
                        
                        Update3(jver, newAccessList);
                        foreach(JObject jmod in jver["Modules"])
                            Update3(jmod, newAccessList);
                        
                        ver["Version"] = jver.ToString();
                    }
                }
            }
            
            if(version < 4)
            {
                gen["Guid"] = Guid.NewGuid().ToString();
                foreach(JObject icon in gen["Icons"])
                {
                    icon["LastModified"] = DateTime.Now.ToString("o");
                }
            }
            
            if(version < 5)
            {
                foreach(JObject app in gen["Applications"])
                {
                    foreach(JObject ver in app["Versions"])
                    {
                        JObject jver = JObject.Parse(ver["Version"].ToString());

                        ver["Namespace"] = jver["NamespaceVersion"];
                    }
                }
            }


            if(version < 8)
            {
                json = gen.ToString();
                json = Update8(json);
                gen = JObject.Parse(json);
            }

            if(version < 9)
            {
                Update9((JObject)gen["Application"]);
            }

            if(version < 10)
            {
                json = gen.ToString();
                json = json.Replace(", Kaenx.Creator", ", Kaenx.Creator.Share");
                gen = JObject.Parse(json);
            }

            return gen.ToString();
        }

        private static void Update3(JObject jver, Dictionary<int, int> newAccessList)
        {
            foreach(JObject para in jver["Parameters"])
            {
                para["Access"] = newAccessList[int.Parse(para["Access"].ToString())];
            }
            foreach(JObject para in jver["ParameterRefs"])
            {
                para["Access"] = newAccessList[int.Parse(para["Access"].ToString())];
            }

            foreach(JObject jdyn in jver["Dynamics"])
            {
                Update3Dyn(jdyn, newAccessList);
            }
        }

        private static void Update3Dyn(JObject jdyn, Dictionary<int, int> newAccessList)
        {
            foreach(JObject jitem in jdyn["Items"])
            {
                switch(jitem["$type"].ToString())
                {
                    case "Kaenx.Creator.Models.Dynamic.DynamicMain, Kaenx.Creator":
                        Update3Dyn(jitem, newAccessList);
                        break;
                    
                    case "Kaenx.Creator.Models.Dynamic.DynChannelIndependent, Kaenx.Creator":
                        Update3Dyn(jitem, newAccessList);
                        break;
                    
                    case "Kaenx.Creator.Models.Dynamic.DynChannel, Kaenx.Creator":
                        if(jitem["Access"] != null)
                            jitem["Access"] = newAccessList[int.Parse(jitem["Access"].ToString())];
                        Update3Dyn(jitem, newAccessList);
                        break;
                    
                    case "Kaenx.Creator.Models.Dynamic.DynParaBlock, Kaenx.Creator":
                        jitem["Access"] = newAccessList[int.Parse(jitem["Access"].ToString())];
                        Update3Dyn(jitem, newAccessList);
                        break;

                    case "Kaenx.Creator.Models.Dynamic.DynChooseBlock, Kaenx.Creator":
                    case "Kaenx.Creator.Models.Dynamic.DynChooseChannel, Kaenx.Creator":
                    case "Kaenx.Creator.Models.Dynamic.DynWhenBlock, Kaenx.Creator":
                    case "Kaenx.Creator.Models.Dynamic.DynWhenChannel, Kaenx.Creator":
                        Update3Dyn(jitem, newAccessList);
                        break;
                }
            }
        }
    
        private static string Update8(string json)
        {
            ModelGeneral general = Newtonsoft.Json.JsonConvert.DeserializeObject<Models.ModelGeneral>(json, new Newtonsoft.Json.JsonSerializerSettings() { TypeNameHandling = Newtonsoft.Json.TypeNameHandling.Objects });
            MainModel main = new MainModel
            {
                Baggages = general.Baggages,
                Catalog = general.Catalog,
                Guid = general.Guid,
                Icons = general.Icons,
                ImportVersion = general.ImportVersion,
                IsOpenKnx = general.IsOpenKnx,
                Languages = general.Languages,
                ManufacturerId = general.ManufacturerId,
                ProjectName = general.ProjectName
            };

            
            Application app = general.Applications[0];
            
            
            AppVersion vers = Newtonsoft.Json.JsonConvert.DeserializeObject<Models.AppVersion>(app.Versions[0].Version, new Newtonsoft.Json.JsonSerializerSettings() { TypeNameHandling = Newtonsoft.Json.TypeNameHandling.Objects });
            main.Application = vers;

            
            Hardware hard =  general.Hardware[0];
            Device dev = hard.Devices[0];

            main.Info = new Info() {
                AppNumber = app.Number,
                BusCurrent = hard.BusCurrent,
                Description = dev.Description,
                HasApplicationProgram = hard.HasApplicationProgram,
                HasApplicationProgram2 = hard.HasApplicationProgram2,
                HasIndividualAddress = hard.HasIndividualAddress,
                IsCoppler = hard.IsCoppler,
                IsIpEnabled = hard.IsIpEnabled,
                IsPowerSupply = hard.IsPowerSupply,
                IsRailMounted = dev.IsRailMounted,
                _maskId = app._maskId,
                Name = hard.Name,
                OrderNumber = dev.OrderNumber,
                SerialNumber = hard.SerialNumber,
                Text = dev.Text,
                Version = hard.Version
            };

            return Newtonsoft.Json.JsonConvert.SerializeObject(main, new Newtonsoft.Json.JsonSerializerSettings() { TypeNameHandling = Newtonsoft.Json.TypeNameHandling.Objects });
        }
    
        private static void Update9(JObject jver)
        {
            foreach(JObject com in jver["ComObjects"])
            {
                com["FlagRead"] = com["FlagRead"].ToString() == "0";
                com["FlagWrite"] = com["FlagWrite"].ToString() == "0";
                com["FlagTrans"] = com["FlagTrans"].ToString() == "0";
                com["FlagComm"] = com["FlagComm"].ToString() == "0";
                com["FlagUpdate"] = com["FlagUpdate"].ToString() == "0";
                com["FlagOnInit"] = com["FlagOnInit"].ToString() == "0";
            }
            foreach(JObject com in jver["ComObjectRefs"])
            {
                com["FlagRead"] = com["FlagRead"].ToString() == "0";
                com["FlagWrite"] = com["FlagWrite"].ToString() == "0";
                com["FlagTrans"] = com["FlagTrans"].ToString() == "0";
                com["FlagComm"] = com["FlagComm"].ToString() == "0";
                com["FlagUpdate"] = com["FlagUpdate"].ToString() == "0";
                com["FlagOnInit"] = com["FlagOnInit"].ToString() == "0";
            }

            
            foreach(JObject mod in jver["Modules"])
            {
                Update9(mod);
            }
        }

        public static void LoadDpts()
        {
            var x = System.Convert.FromBase64String(Strings.dpts);
            string y = System.Text.Encoding.UTF8.GetString(x);
            
            DPTs = new ObservableCollection<Models.DataPointType>();
            XDocument xdoc = XDocument.Parse(y);
            IEnumerable<XElement> xdpts = xdoc.Descendants(XName.Get("DatapointType"));
            
            DPTs.Add(new Models.DataPointType() {
                Name = "Empty (only for Ref)",
                Number = "0",
                Size = 0
            });

            foreach(XElement xdpt in xdpts)
            {
                Models.DataPointType dpt = new Models.DataPointType();
                dpt.Name = xdpt.Attribute("Name").Value + " " + xdpt.Attribute("Text").Value;
                dpt.Number = xdpt.Attribute("Number").Value;
                dpt.Size = int.Parse(xdpt.Attribute("SizeInBit").Value);

                IEnumerable<XElement> xsubs = xdpt.Descendants(XName.Get("DatapointSubtype"));

                foreach(XElement xsub in xsubs)
                {
                    Models.DataPointSubType dpst = new Models.DataPointSubType();
                    dpst.Name = dpt.Number + "." + Fill(xsub.Attribute("Number").Value, 3, "0") + " " + xsub.Attribute("Text").Value;
                    dpst.Number = xsub.Attribute("Number").Value;
                    dpst.ParentNumber = dpt.Number;
                    dpt.SubTypes.Add(dpst);
                }

                DPTs.Add(dpt);
            }
        }

        public static void LoadBcus()
        {
            var x = System.Convert.FromBase64String(Strings.masks);
            string y = System.Text.Encoding.UTF8.GetString(x);
            BCUs = new ObservableCollection<Models.MaskVersion>();

            XDocument xdoc = XDocument.Parse(y);
            foreach(XElement xmask in xdoc.Root.Elements())
            {
                Models.MaskVersion mask = new Models.MaskVersion();
                mask.Id = xmask.Attribute("Id").Value;
                mask.MediumTypes = xmask.Attribute("MediumTypeRefId").Value;
                mask.ManagementModel = xmask.Attribute("ManagementModel").Value;
                if(xmask.Attribute("OtherMediumTypeRefId") != null) mask.MediumTypes += " " + xmask.Attribute("OtherMediumTypeRefId").Value;

                string eleStr = xmask.ToString();
                if (eleStr.Contains("<Procedure ProcedureType=\"Load\""))
                {
                    XElement prodLoad = xmask.Descendants(XName.Get("Procedure")).First(p => p.Attribute("ProcedureType")?.Value == "Load");
                    if (prodLoad.ToString().Contains("<LdCtrlMerge"))
                        mask.Procedure = Models.ProcedureTypes.Merged;
                    else
                        mask.Procedure = Models.ProcedureTypes.Default;
                } else
                {
                    mask.Procedure = Models.ProcedureTypes.Product;
                }

                if(mask.Procedure != Models.ProcedureTypes.Product)
                {
                    if (eleStr.Contains("<LdCtrlAbsSegment"))
                    {
                        mask.Memory = Models.MemoryTypes.Absolute;
                    }
                    else if (eleStr.Contains("<LdCtrlWriteRelMem"))
                    {
                        mask.Memory = Models.MemoryTypes.Relative;
                    }
                    else if (eleStr.Contains("<LdCtrlWriteMem"))
                    {
                        mask.Memory = Models.MemoryTypes.Absolute;
                    }
                    else
                    {
                        continue;
                    }
                }

                if(xmask.Descendants(XName.Get("Procedures")).Count() > 0) {
                    foreach(XElement xproc in xmask.Element(XName.Get("HawkConfigurationData")).Element(XName.Get("Procedures")).Elements()) {
                        Models.Procedure proc = new Models.Procedure();
                        proc.Type = xproc.Attribute("ProcedureType").Value;
                        proc.SubType = xproc.Attribute("ProcedureSubType").Value;

                        System.Text.StringBuilder sb = new();

                        foreach (XNode node in xproc.Nodes())
                            sb.Append(node.ToString() + "\r\n");

                        proc.Controls = sb.ToString();
                        mask.Procedures.Add(proc);
                    }
                }

                BCUs.Add(mask);
            }
        }

        private static string Fill(string input, int length, string fill)
        {
            for(int i = input.Length; i < length; i++)
            {
                input = fill + input;
            }
            return input;
        }

        public static void LoadVersion(MainModel general, Models.IVersionBase mod)
        {   
            if (!string.IsNullOrEmpty(general.Info._maskId))
            {
                general.Info.Mask = BCUs.Single(bcu => bcu.Id == general.Info._maskId);
            }

            Paras = new Dictionary<long, Parameter>();
            foreach(Parameter para in mod.Parameters)
                Paras.Add(para.UId, para);
            if(mod.Parameters.Count == 0)
                mod.LastParameterId = 0;
            else
                mod.LastParameterId = mod.Parameters.OrderByDescending(p => p.Id).First().Id;

            ParaRefs = new Dictionary<long, ParameterRef>();
            foreach(ParameterRef pref in mod.ParameterRefs)
                ParaRefs.Add(pref.UId, pref);
            if(mod.ParameterRefs.Count == 0)
                mod.LastParameterRefId = 0;
            else
            mod.LastParameterRefId = mod.ParameterRefs.OrderByDescending(p => p.Id).First().Id;

            Coms = new Dictionary<long, ComObject>();
            foreach(ComObject com in mod.ComObjects)
                Coms.Add(com.UId, com);

            ComRefs = new Dictionary<long, ComObjectRef>();
            foreach(ComObjectRef cref in mod.ComObjectRefs)
                ComRefs.Add(cref.UId, cref);

            if(general.Application == mod) {
                if(general.Application._addressMemoryId != -1)
                    general.Application.AddressMemoryObject = general.Application.Memories.SingleOrDefault(m => m.UId == general.Application._addressMemoryId);

                if(general.Application._assocMemoryId != -1)
                    general.Application.AssociationMemoryObject = general.Application.Memories.SingleOrDefault(m => m.UId == general.Application._assocMemoryId);
                    
                if(general.Application._comMemoryId != -1)
                    general.Application.ComObjectMemoryObject = general.Application.Memories.SingleOrDefault(m => m.UId == general.Application._comMemoryId);
            
                foreach(Models.ParameterType ptype in general.Application.ParameterTypes)
                {
                    if(ptype.Type == Models.ParameterTypes.Picture && ptype._baggageUId != -1)
                        ptype.BaggageObject = general.Baggages.SingleOrDefault(b => b.UId == ptype._baggageUId);

                    if(ptype.Type == Models.ParameterTypes.Enum)
                    {
                        foreach(ParameterTypeEnum penu in ptype.Enums)
                        {
                            if(penu.UseIcon && penu._iconId != -1)
                                penu.IconObject = general.Icons.SingleOrDefault(i => i.UId == penu._iconId);
                        }
                    }
                }
                
                SetSubCatalogItems(general.Catalog[0]);
            } else {
                Models.Module modu = mod as Models.Module;
                if(modu._parameterBaseOffsetUId != -1)
                    modu.ParameterBaseOffset = modu.Arguments.SingleOrDefault(m => m.UId == modu._parameterBaseOffsetUId);
                
                if(modu._comObjectBaseNumberUId != -1)
                    modu.ComObjectBaseNumber = modu.Arguments.SingleOrDefault(m => m.UId == modu._comObjectBaseNumberUId);
            }

            foreach(Models.Parameter para in mod.Parameters)
            {
                if (para._memoryId != -1)
                    para.SaveObject = general.Application.Memories.SingleOrDefault(m => m.UId == para._memoryId);
                    
                if (para._parameterType != -1)
                    para.ParameterTypeObject = general.Application.ParameterTypes.SingleOrDefault(p => p.UId == para._parameterType);

                if(para.IsInUnion && para._unionId != -1)
                    para.UnionObject = mod.Unions.SingleOrDefault(u => u.UId == para._unionId);
            }

            foreach(Models.Union union in mod.Unions)
            {
                if (union._memoryId != -1)
                    union.MemoryObject = general.Application.Memories.SingleOrDefault(u => u.UId == union._memoryId);
            }

            foreach(Models.ParameterRef pref in mod.ParameterRefs)
            {
                if (pref._parameter != -1)
                {
                    try{
                        pref.ParameterObject = Paras[pref._parameter];
                    } catch {
                        //TODO Translate
                        //MessageBox.Show($"Für ParameterRef {pref.Name} konnte der Parameter nicht zugeordnet werden.");
                    }
                }
            }

            foreach(Models.ComObject com in mod.ComObjects)
            {
                if (!string.IsNullOrEmpty(com._typeNumber) && DPTs != null)
                    com.Type = DPTs.Single(d => d.Number == com._typeNumber);
                    
                if(!string.IsNullOrEmpty(com._subTypeNumber) && com.Type != null)
                    com.SubType = com.Type.SubTypes.Single(d => d.Number == com._subTypeNumber);
                    
                if(mod.IsComObjectRefAuto && com.UseTextParameter && com._parameterRef != -1)
                    com.ParameterRefObject = ParaRefs[com._parameterRef];
            }

            foreach(Models.ComObjectRef cref in mod.ComObjectRefs)
            {
                if(cref._comObject != -1)
                {
                    try{
                    cref.ComObjectObject = Coms[cref._comObject];
                    } catch {
                        //TODO Translate
                        //MessageBox.Show($"Für ComObjectRef {cref.Name} konnte das ComObject nicht zugeordnet werden.");
                    }
                }

                if (!string.IsNullOrEmpty(cref._typeNumber) && DPTs != null)
                    cref.Type = DPTs.Single(d => d.Number == cref._typeNumber);
                    
                if(!string.IsNullOrEmpty(cref._subTypeNumber) && cref.Type != null)
                    cref.SubType = cref.Type.SubTypes.Single(d => d.Number == cref._subTypeNumber);

                if(!mod.IsComObjectRefAuto && cref.UseTextParameter && cref._parameterRef != -1)
                    cref.ParameterRefObject = ParaRefs[cref._parameterRef];
            }

            if(mod is Models.Module mod2)
            {
                if(mod2._parameterBaseOffsetUId != -1)
                    mod2.ParameterBaseOffset = mod2.Arguments.SingleOrDefault(a => a.UId == mod2._parameterBaseOffsetUId);

                if(mod2._comObjectBaseNumberUId != -1)
                    mod2.ComObjectBaseNumber = mod2.Arguments.SingleOrDefault(a => a.UId == mod2._comObjectBaseNumberUId);
            }

            if(mod.Dynamics.Count > 0)
                LoadSubDyn(general, mod.Dynamics[0], mod);
            
            foreach(Models.Module mod3 in mod.Modules)
                LoadVersion(general, mod3);
        }

        private static void SetSubCatalogItems(Models.CatalogItem parent)
        {
            foreach(Models.CatalogItem item in parent.Items)
            {
                item.Parent = parent;
                SetSubCatalogItems(item);
            }
        }

        private static void LoadSubDyn(MainModel general, Models.Dynamic.IDynItems dyn, IVersionBase mod)
        {
            foreach (Models.Dynamic.IDynItems item in dyn.Items)
            {
                item.Parent = dyn;

                switch(item)
                {
                    case Models.Dynamic.DynChannel dch:
                        if(dch.UseTextParameter)
                            dch.ParameterRefObject = ParaRefs[dch._parameter];
                        if(dch.UseIcon && dch._iconId != -1)
                            dch.IconObject = general.Icons.SingleOrDefault(i => i.UId == dch._iconId);
                        break;

                    case Models.Dynamic.DynParameter dp:
                        if (dp._parameter != -1 && ParaRefs.ContainsKey(dp._parameter))
                            dp.ParameterRefObject = ParaRefs[dp._parameter];
                        if(dp.HasHelptext)
                            dp.Helptext = general.Application.Helptexts.FirstOrDefault(p => p.UId == dp._helptextId);
                        if(dp.UseIcon && dp._iconId != -1)
                            dp.IconObject = general.Icons.SingleOrDefault(i => i.UId == dp._iconId);
                        break;

                    case Models.Dynamic.DynChooseBlock dcb:
                        if (dcb._parameterRef != -1)
                        {
                            if(!dcb.IsGlobal && ParaRefs.ContainsKey(dcb._parameterRef))
                                dcb.ParameterRefObject = ParaRefs[dcb._parameterRef];
                            else
                                dcb.ParameterRefObject = general.Application.ParameterRefs.SingleOrDefault(p => p.UId == dcb._parameterRef);
                        }
                        break;

                    case Models.Dynamic.DynChooseChannel dcc:
                        if (dcc._parameterRef != -1)
                            dcc.ParameterRefObject = ParaRefs[dcc._parameterRef];
                        break;

                    case Models.Dynamic.DynComObject dco:
                        if (dco._comObjectRef != -1)
                            dco.ComObjectRefObject = ComRefs[dco._comObjectRef];
                        break;

                    case Models.Dynamic.DynParaBlock dpb:
                        if(dpb.UseParameterRef && dpb._parameterRef != -1)
                            dpb.ParameterRefObject = ParaRefs[dpb._parameterRef];
                        if(dpb.UseTextParameter && dpb._textRef != -1)
                            dpb.TextRefObject = ParaRefs[dpb._textRef];
                        if(dpb.UseIcon && dpb._iconId != -1)
                            dpb.IconObject = general.Icons.SingleOrDefault(i => i.UId == dpb._iconId);
                        break;

                    case Models.Dynamic.DynSeparator ds:
                        if(ds.UseIcon && ds._iconId != -1)
                            ds.IconObject = general.Icons.SingleOrDefault(i => i.UId == ds._iconId);
                        break;

                    case Models.Dynamic.DynModule dm:
                        if(dm._module != -1)
                        {
                            dm.ModuleObject = mod.Modules.Single(m => m.UId == dm._module); // general.Application.Modules.Single(m => m.UId == dm._module);
                            foreach(Models.Dynamic.DynModuleArg arg in dm.Arguments)
                            {
                                if(arg._argId != -1)
                                    arg.Argument = dm.ModuleObject.Arguments.Single(a => a.UId == arg._argId);
                                if(arg.UseAllocator && arg._allocId != -1)
                                    arg.Allocator = mod.Allocators.SingleOrDefault(a => a.UId == arg._allocId);
                            }
                        }
                        break;

                    case Models.Dynamic.DynAssign dass:
                        if(dass._targetUId != -1)
                            dass.TargetObject = ParaRefs[dass._targetUId];
                        if(string.IsNullOrEmpty(dass.Value) && dass._sourceUId != -1)
                            dass.SourceObject = ParaRefs[dass._sourceUId];
                        if(string.IsNullOrEmpty(dass.Value) && dass._sourceUId == -1)
                        {
                            //TODO show some message
                        }
                        break;

                    case Models.Dynamic.DynRepeat dre:
                        if(dre.UseParameterRef && dre._parameterUId != -1)
                            dre.ParameterRefObject = ParaRefs[dre._parameterUId];
                        break;
                    
                    case Models.Dynamic.DynButton db:
                        if(db.UseTextParameter && db._textRef != -1)
                            db.TextRefObject = ParaRefs[db._textRef];
                        if(db.UseIcon && db._iconId != -1)
                            db.IconObject = general.Icons.SingleOrDefault(i => i.UId == db._iconId);
                        break;

                }

                if (item.Items != null)
                    LoadSubDyn(general, item, mod);
            }
        }
    
        public static void GetModules(Models.Dynamic.IDynItems item, List<Models.Dynamic.DynModule> mods, long repeater = 1)
        {
            if(item is Models.Dynamic.DynModule dm)
            {
                for(int i = 0; i < repeater; i++)
                    mods.Add(dm);
            }

            if(item.Items == null) return;

            long srepeat = repeater;
            if(item is Models.Dynamic.DynRepeat dr)
            {
                srepeat = dr.Count;
                if(dr.ParameterRefObject != null)
                {
                    srepeat += long.Parse(dr.ParameterRefObject.ParameterObject.ParameterTypeObject.Max);
                }
            }

            foreach(Models.Dynamic.IDynItems i in item.Items)
                GetModules(i, mods, srepeat);
        }

        public static int GetNextFreeUId(object list, int start = 1) {
            int id = start;

            if(list is System.Collections.ObjectModel.ObservableCollection<Parameter>) {
                while((list as System.Collections.ObjectModel.ObservableCollection<Parameter>).Any(i => i.UId == id))
                    id++;
            }else if(list is System.Collections.ObjectModel.ObservableCollection<ParameterRef>) {
                while((list as System.Collections.ObjectModel.ObservableCollection<ParameterRef>).Any(i => i.UId == id))
                    id++;
            }else if(list is System.Collections.ObjectModel.ObservableCollection<ComObject>) {
                while((list as System.Collections.ObjectModel.ObservableCollection<ComObject>).Any(i => i.UId == id))
                    id++;
            }else if(list is System.Collections.ObjectModel.ObservableCollection<ComObjectRef>) {
                while((list as System.Collections.ObjectModel.ObservableCollection<ComObjectRef>).Any(i => i.UId == id))
                    id++;
            }else if(list is System.Collections.ObjectModel.ObservableCollection<Memory>) {
                while((list as System.Collections.ObjectModel.ObservableCollection<Memory>).Any(i => i.UId == id))
                    id++;
            }else if(list is System.Collections.ObjectModel.ObservableCollection<ParameterType>) {
                while((list as System.Collections.ObjectModel.ObservableCollection<ParameterType>).Any(i => i.UId == id))
                    id++;
            }else if(list is System.Collections.ObjectModel.ObservableCollection<Union>) {
                while((list as System.Collections.ObjectModel.ObservableCollection<Union>).Any(i => i.UId == id))
                    id++;
            }else if(list is System.Collections.ObjectModel.ObservableCollection<Module>) {
                while((list as System.Collections.ObjectModel.ObservableCollection<Module>).Any(i => i.UId == id))
                    id++;
            }else if(list is System.Collections.ObjectModel.ObservableCollection<Argument>) {
                while((list as System.Collections.ObjectModel.ObservableCollection<Argument>).Any(i => i.UId == id))
                    id++;
            }else if(list is System.Collections.ObjectModel.ObservableCollection<Allocator>) {
                while((list as System.Collections.ObjectModel.ObservableCollection<Allocator>).Any(i => i.UId == id))
                    id++;
            } else if(list is System.Collections.ObjectModel.ObservableCollection<Baggage>) {
                while((list as System.Collections.ObjectModel.ObservableCollection<Baggage>).Any(i => i.UId == id))
                    id++;
            } else if(list is System.Collections.ObjectModel.ObservableCollection<Message>) {
                while((list as System.Collections.ObjectModel.ObservableCollection<Message>).Any(i => i.UId == id))
                    id++;
            } else if(list is System.Collections.ObjectModel.ObservableCollection<Helptext>) {
                while((list as System.Collections.ObjectModel.ObservableCollection<Helptext>).Any(i => i.UId == id))
                    id++;
            } else if(list is System.Collections.ObjectModel.ObservableCollection<Icon>) {
                while((list as System.Collections.ObjectModel.ObservableCollection<Icon>).Any(i => i.UId == id))
                    id++;
            } else if(list is System.Collections.ObjectModel.ObservableCollection<OpenKnxModule>) {
                while((list as System.Collections.ObjectModel.ObservableCollection<OpenKnxModule>).Any(i => i.UId == id))
                    id++;
            } else {
                throw new Exception("Can't get NextFreeUId. Type not implemented.");
            }
            return id;
        }

        public static long GetNextFreeId(IVersionBase vbase, string list, long start = 1) {
            long id = start;

            if(list == "Parameters") {
                return ++vbase.LastParameterId;
            } else if(list == "ParameterRefs") {
                return ++vbase.LastParameterRefId;
            } else {
                var x = vbase.GetType().GetProperty(list).GetValue(vbase);
                if(x is System.Collections.ObjectModel.ObservableCollection<ComObject> lc) {
                    while(lc.Any(i => i.Id == id))
                        id++;
                }else if(x is System.Collections.ObjectModel.ObservableCollection<ComObjectRef> lcr) {
                    while(lcr.Any(i => i.Id == id))
                        id++;
                }else if(x is System.Collections.ObjectModel.ObservableCollection<Argument> la) {
                    while(la.Any(i => i.Id == id))
                        id++;
                }else if(x is System.Collections.ObjectModel.ObservableCollection<Module> lm) {
                    while(lm.Any(i => i.Id == id))
                        id++;
                }else if(x is System.Collections.ObjectModel.ObservableCollection<Message> ls) {
                    while(ls.Any(i => i.Id == id))
                        id++;
                }else if(x is System.Collections.ObjectModel.ObservableCollection<Allocator> lac) {
                    while(lac.Any(i => i.Id == id))
                        id++;
                }
                return id;
            }
        }
        
        public static void CheckIds(AppVersion version)
        {
            CheckIdsModule(version, version);
        }

        private static void CheckIdsModule(AppVersion version, IVersionBase vbase, IVersionBase vparent = null)
        {
            foreach(Parameter para in vbase.Parameters)
                if(para.Id == -1) para.Id = GetNextFreeId(vbase, "Parameters");

            foreach(ParameterRef pref in vbase.ParameterRefs)
                if(pref.Id == -1) pref.Id = GetNextFreeId(vbase, "ParameterRefs");

            foreach(ComObject com in vbase.ComObjects)
                if(com.Id == -1) com.Id = GetNextFreeId(vbase, "ComObjects", 0);

            foreach(ComObjectRef cref in vbase.ComObjectRefs)
                if(cref.Id == -1) cref.Id = GetNextFreeId(vbase, "ComObjectRefs", 0);

            if(vbase is Module mod)
            {
                if(mod.Id == -1)
                    mod.Id = GetNextFreeId(vparent, "Modules");

                foreach(Argument arg in mod.Arguments)
                    if(arg.Id == -1) arg.Id = GetNextFreeId(vbase, "Arguments");
            }

            counterBlock = 1;
            counterSeparator = 1;
            CheckDynamicIds(version.Dynamics[0]);

            foreach(Models.Module xmod in vbase.Modules)
                CheckIdsModule(version, xmod, vbase);
        }

        private static int counterBlock = 1;
        private static int counterSeparator = 1;
        public static void CheckDynamicIds(IDynItems parent)
        {
            foreach(IDynItems item in parent.Items)
            {
                switch(item)
                {
                    case DynParaBlock dpb:
                        dpb.Id = counterBlock++;
                        break;

                    case DynSeparator ds:
                        ds.Id = counterSeparator++;
                        break;
                }

                if(item.Items != null)
                    CheckDynamicIds(item);
            }
        }
    }
}