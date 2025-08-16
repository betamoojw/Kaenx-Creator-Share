using Kaenx.Creator.Models;
using Kaenx.Creator.Models.Dynamic;
using OpenKNX.Toolbox.Sign;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Kaenx.Creator.Classes
{
    public class ExportHelper
    {
        MainModel general;
        XDocument doc;
        string appVersion;
        string appVersionMod;
        string currentNamespace;
        string headerPath;
        List<Icon> iconsApp = new List<Icon>();
        List<string> buttonScripts;
        ObservableCollection<PublishAction> actions;

        public ExportHelper(MainModel g)
        {
            general = g;
        }

        public ExportHelper(MainModel g, string hP)
        {
            general = g;
            headerPath = hP;
        }

        string currentLang = "";
        private Dictionary<string, Dictionary<string, Dictionary<string, string>>> languages {get;set;} = null;
 
        private void AddTranslation(string lang, string id, string attr, string value) {
            if(string.IsNullOrEmpty(value)) return;
            if(!languages.ContainsKey(lang)) languages.Add(lang, new Dictionary<string, Dictionary<string, string>>());
            if(!languages[lang].ContainsKey(id)) languages[lang].Add(id, new Dictionary<string, string>());
            if(!languages[lang][id].ContainsKey(attr)) languages[lang][id].Add(attr, value);
        }

        public bool ExportEts(ObservableCollection<PublishAction> _actions, bool isDevMode)
        {
            actions = _actions;
            string Manu = "M-" + GetManuId();

            System.IO.Directory.CreateDirectory(GetRelPath());
            if (System.IO.Directory.Exists(GetRelPath("Temp")))
                System.IO.Directory.Delete(GetRelPath("Temp"), true);

            System.IO.Directory.CreateDirectory(GetRelPath("Temp"));
            System.IO.Directory.CreateDirectory(GetRelPath("Temp", Manu));

            currentNamespace = $"http://knx.org/xml/project/{general.Application.NamespaceVersion}";

            Dictionary<string, string> ProductIds = new Dictionary<string, string>();
            Dictionary<string, string> HardwareIds = new Dictionary<string, string>();
            List<Baggage> baggagesManu = new List<Baggage>();
            bool exportIcons = false;

            #region XML Applications
            Log($"Exportiere Applikation");
            XElement xmanu = null;
            XElement xlanguages = null;
            
            AppVersion ver = general.Application;
            Log($"Exportiere AppVersion: {ver.Name} {ver.NameText}");
            languages = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
            xmanu = CreateNewXML(Manu);
            XElement xapps = new XElement(Get("ApplicationPrograms"));
            xmanu.Add(xapps);

            appVersion = $"{Manu}_A-{GetAppId(general.Info.AppNumber)}-{ver.Number:X2}";
            appVersion += "-0000";
            appVersionMod = appVersion;

            currentLang = ver.DefaultLanguage;
            foreach(Models.Translation trans in ver.Text)
                AddTranslation(trans.Language.CultureCode, appVersion, "Name", trans.Text);

            XElement xunderapp = new XElement(Get("Static"));
            XElement xapp = new XElement(Get("ApplicationProgram"), xunderapp);
            xapps.Add(xapp);
            xapp.SetAttributeValue("Id", appVersion);
            int appnumber = general.Info.AppNumber;
            if(general.IsOpenKnx) appnumber |= general.ManufacturerId << 8;
            xapp.SetAttributeValue("ApplicationNumber", appnumber);
            xapp.SetAttributeValue("ApplicationVersion", ver.Number.ToString());
            xapp.SetAttributeValue("ProgramType", "ApplicationProgram");
            xapp.SetAttributeValue("MaskVersion", general.Info.Mask.Id);
            xapp.SetAttributeValue("Name", GetDefaultLanguage(ver.Text));
            xapp.SetAttributeValue("DefaultLanguage", currentLang);
            xapp.SetAttributeValue("LoadProcedureStyle", $"{general.Info.Mask.Procedure}Procedure");
            xapp.SetAttributeValue("PeiType", "0");
            xapp.SetAttributeValue("DynamicTableManagement", "false"); //TODO check when to add
            xapp.SetAttributeValue("Linkable", "false"); //TODO check when to add
            if(general.Info.IsIpEnabled)
                xapp.SetAttributeValue("IPConfig", ver.IpConfig);
            
            if (ver.IsBusInterfaceActive && ver.BusInterfaceCounter > 0)
            {
                xapp.SetAttributeValue("AdditionalAddressesCount", ver.BusInterfaceCounter);
            }
            if(ver.IsSecureActive)
            {
                xapp.SetAttributeValue("IsSecureEnabled", "true");
                if(ver.IsBusInterfaceActive)
                {
                    // TODO get correct value
                    xapp.SetAttributeValue("MaxUserEntries", "1");
                }
            }

            buttonScripts = new List<string>();
            iconsApp = new List<Icon>();
            List<Baggage> baggagesApp = new List<Baggage>();
            if(ver.IsHelpActive)
            {
                if(ver.NamespaceVersion == 14)
                {
                    xapp.SetAttributeValue("ContextHelpFile", "HelpFile_" + ver.DefaultLanguage + ".zip");
                } else {
                    xapp.SetAttributeValue("ContextHelpFile", $"{Manu}_BG--{GetEncoded("HelpFile_" + ver.DefaultLanguage + ".zip")}");
                }
                ExportHelptexts(ver, Manu, baggagesManu, baggagesApp);
            }

            if(ver.IsPreETS4)
            {
                xapp.SetAttributeValue("PreEts4Style", "true"); //TODO check when to add
                xapp.SetAttributeValue("ConvertedFromPreEts4Data", "true"); //TODO check when to add
            }

            if(!string.IsNullOrEmpty(ver.ReplacesVersions)) xapp.SetAttributeValue("ReplacesVersions", ver.ReplacesVersions);

            switch (currentNamespace)
            {
                case "http://knx.org/xml/project/11":
                    xapp.SetAttributeValue("MinEtsVersion", "4.0");
                    break;
                case "http://knx.org/xml/project/12":
                case "http://knx.org/xml/project/13":
                case "http://knx.org/xml/project/14":
                case "http://knx.org/xml/project/20":
                    xapp.SetAttributeValue("MinEtsVersion", "5.0");
                    break;
                case "http://knx.org/xml/project/21":
                    xapp.SetAttributeValue("MinEtsVersion", "6.0");
                    break;
            }

            Helper.CheckIds(ver);

            XElement temp;
            ExportSegments(ver, xunderapp);
            
            StringBuilder headers = new StringBuilder();
            headers.AppendLine("#pragma once");
            headers.AppendLine();

            headers.AppendLine(@"#define paramDelay(time) (uint32_t)( \
            (time & 0xC000) == 0xC000 ? (time & 0x3FFF) * 100 : \
            (time & 0xC000) == 0x0000 ? (time & 0x3FFF) * 1000 : \
            (time & 0xC000) == 0x4000 ? (time & 0x3FFF) * 60000 : \
            (time & 0xC000) == 0x8000 ? ((time & 0x3FFF) > 1000 ? 3600000 : \
                                            (time & 0x3FFF) * 3600000 ) : 0 )");

            #region ParamTypes/Baggages
            Log($"Exportiere ParameterTypes: {ver.ParameterTypes.Count}x");
            temp = new XElement(Get("ParameterTypes"));
            foreach (ParameterType type in ver.ParameterTypes)
            {
                //Log($"    - ParameterType {type.Name}x");
                string id = appVersion + "_PT-" + GetEncoded(type.Name);
                XElement xtype = new XElement(Get("ParameterType"));
                xtype.SetAttributeValue("Id", id);
                xtype.SetAttributeValue("Name", type.Name);
                XElement xcontent = null;

                switch (type.Type)
                {
                    case ParameterTypes.None:
                        xcontent = new XElement(Get("TypeNone"));
                        break;

                    case ParameterTypes.Text:
                        xcontent = new XElement(Get("TypeText"));
                        break;

                    case ParameterTypes.NumberInt:
                    case ParameterTypes.NumberUInt:
                    case ParameterTypes.Float_DPT9:
                    case ParameterTypes.Float_IEEE_Double:
                    case ParameterTypes.Float_IEEE_Single:
                        if(type.Type == ParameterTypes.NumberUInt || type.Type == ParameterTypes.NumberInt)
                            xcontent = new XElement(Get("TypeNumber"));
                        else
                            xcontent = new XElement(Get("TypeFloat"));

                        switch(type.Type)
                        {
                            case ParameterTypes.NumberUInt:
                                xcontent.SetAttributeValue("Type", "unsignedInt");
                                break;
                            
                            case ParameterTypes.NumberInt:
                                xcontent.SetAttributeValue("Type", "signedInt");
                                break;

                            case ParameterTypes.Float_DPT9:
                                xcontent.SetAttributeValue("Encoding", "DPT 9");
                                break;

                            case ParameterTypes.Float_IEEE_Single:
                                xcontent.SetAttributeValue("Encoding", "IEEE-754 Single");
                                break;

                            case ParameterTypes.Float_IEEE_Double:
                                xcontent.SetAttributeValue("Encoding", "IEEE-754 Double");
                                break;

                            default:
                                throw new Exception("Unbekannter ParameterType: " + type.Type.ToString());
                        }
                        xcontent.SetAttributeValue("minInclusive", type.Min.Replace(",", "."));
                        xcontent.SetAttributeValue("maxInclusive", type.Max.Replace(",", "."));
                        if(type.Increment != "1")
                            xcontent.SetAttributeValue("Increment", type.Increment.Replace(",", "."));
                        if(type.UIHint != "None" && !string.IsNullOrEmpty(type.UIHint))
                            xcontent.SetAttributeValue("UIHint", type.UIHint);
                        if(type.DisplayOffset != "0")
                            xcontent.SetAttributeValue("DisplayOffset", type.DisplayOffset);
                        if(type.DisplayFactor != "1")
                            xcontent.SetAttributeValue("DisplayFactor", type.DisplayFactor);
                        break;

                    case ParameterTypes.Enum:
                        xcontent = new XElement(Get("TypeRestriction"));
                        xcontent.SetAttributeValue("Base", "Value");
                        foreach (ParameterTypeEnum enu in type.Enums)
                        {
                            XElement xenu = new XElement(Get("Enumeration"));
                            xenu.SetAttributeValue("Text", GetDefaultLanguage(enu.Text));
                            xenu.SetAttributeValue("Value", enu.Value);
                            xenu.SetAttributeValue("Id", $"{id}_EN-{enu.Value}");
                            xcontent.Add(xenu);
                            if(enu.Translate)
                                foreach(Models.Translation trans in enu.Text) AddTranslation(trans.Language.CultureCode, $"{id}_EN-{enu.Value}", "Text", trans.Text);
                        
                            if(enu.UseIcon)
                            {
                                xenu.SetAttributeValue("Icon", enu.IconObject.Name);
                                if(!iconsApp.Contains(enu.IconObject))
                                    iconsApp.Add(enu.IconObject);
                            }

                            if(type.ExportInHeader)
                                headers.AppendLine($"#define PT_{type.Name}_{enu.Name} {enu.Value}");
                        }
                        break;

                    case ParameterTypes.Picture:
                    {
                        xcontent = new XElement(Get("TypePicture"));
                        Baggage bag2 = type.BaggageObject.Copy();

                        if(general.IsOpenKnx)
                        {
                            switch(type.BaggageObject.TargetPath)
                            {
                                case "root":
                                    bag2.TargetPath = "";
                                    break;

                                case "openknxid":
                                {
                                    bag2.TargetPath = general.ManufacturerId.ToString("X2");
                                    break;
                                }

                                case "openknxapp":
                                {
                                    bag2.TargetPath = Path.Combine(general.ManufacturerId.ToString("X2"), general.Info.AppNumber.ToString("X2"));
                                    break;
                                }
                            }
                        }
                        
                        xcontent.SetAttributeValue("RefId", $"M-{GetManuId()}_BG-{GetEncoded(bag2.TargetPath)}-{GetEncoded(type.BaggageObject.Name + type.BaggageObject.Extension)}");
                        if (!baggagesApp.Any(b => b.TargetPath == bag2.TargetPath && b.Name == bag2.Name && b.Extension == bag2.Extension))
                            baggagesApp.Add(bag2);
                        if(!string.IsNullOrEmpty(type.UIHint))
                            xcontent.SetAttributeValue("HorizontalAlignment", type.UIHint);
                        break;
                    }

                    case ParameterTypes.IpAddress:
                        xcontent = new XElement(Get("TypeIPAddress"));
                        xcontent.SetAttributeValue("AddressType", type.UIHint);
                        if(type.Increment == "IPv6")
                        {
                            xcontent.SetAttributeValue("Version", type.Increment);
                        }
                        break;

                    case ParameterTypes.Color:
                        xcontent = new XElement(Get("TypeColor"));
                        xcontent.SetAttributeValue("Space", type.UIHint);
                        break;

                    case ParameterTypes.RawData:
                        xcontent = new XElement(Get("TypeRawData"));
                        xcontent.SetAttributeValue("MaxSize", type.Max);
                        break;

                    case ParameterTypes.Date:
                        xcontent = new XElement(Get("TypeDate"));
                        xcontent.SetAttributeValue("Encoding", type.UIHint);
                        if(!type.OtherValue)
                            xcontent.SetAttributeValue("DisplayTheYear", "false");
                        break;

                    case ParameterTypes.Time:
                        xcontent = new XElement(Get("TypeTime"));
                        xcontent.SetAttributeValue("Unit", type.Increment);
                        if(!string.IsNullOrEmpty(type.UIHint))
                            xcontent.SetAttributeValue("UIHint", type.UIHint);
                        xcontent.SetAttributeValue("minInclusive", type.Min.Replace(",", "."));
                        xcontent.SetAttributeValue("maxInclusive", type.Max.Replace(",", "."));
                        break;

                    default:
                        throw new Exception("Unbekannter Parametertyp: " + type.Type);
                }

                if (xcontent != null && 
                    xcontent.Name.LocalName != "TypeFloat" &&
                    xcontent.Name.LocalName != "TypeNone" &&
                    xcontent.Name.LocalName != "TypePicture" &&
                    xcontent.Name.LocalName != "TypeColor" &&
                    xcontent.Name.LocalName != "TypeDate" &&
                    xcontent.Name.LocalName != "TypeRawData" &&
                    xcontent.Name.LocalName != "TypeIPAddress")
                {
                    xcontent.SetAttributeValue("SizeInBit", type.SizeInBit);
                }
                if (xcontent != null)
                    xtype.Add(xcontent);
                temp.Add(xtype);
            }
            xunderapp.Add(temp);
            XElement xextension = new XElement(Get("Extension"));

            if (baggagesApp.Count > 0)
            {
                foreach(Baggage bag in baggagesApp)
                {
                    XElement xbag = new XElement(Get("Baggage"));
                    xbag.SetAttributeValue("RefId", $"M-{GetManuId()}_BG-{GetEncoded(bag.TargetPath)}-{GetEncoded(bag.Name + bag.Extension)}");
                    xextension.Add(xbag);

                    if (!baggagesManu.Any(b => b.TargetPath == bag.TargetPath && b.Name == bag.Name && b.Extension == bag.Extension))
                        baggagesManu.Add(bag);
                }
            }
            #endregion

            headers.AppendLine("//--------------------Allgemein---------------------------");
            if(general.IsOpenKnx)
            {
                headers.AppendLine($"#define MAIN_OpenKnxId 0x{general.ManufacturerId:X2}");
                headers.AppendLine($"#define MAIN_ApplicationNumber 0x{general.Info.AppNumber:X2}");
            } else {
                //headers.AppendLine($"#define MAIN_OpenKnxId 0x{(app.Number >> 8):X2}");
                headers.AppendLine($"#define MAIN_ApplicationNumber 0x{general.Info.AppNumber:X4}");
            }
            headers.AppendLine($"#define MAIN_ApplicationVersion 0x{ver.Number:X2}");
            headers.AppendLine($"#define MAIN_OrderNumber \"{general.Info.OrderNumber}\"");
            if(ver.Memories.Count > 0)
                headers.AppendLine($"#define MAIN_ParameterSize {ver.Memories[0].Size}");
            headers.AppendLine($"#define MAIN_MaxKoNumber {ver.HighestComNumber}");
            headers.AppendLine();
            headers.AppendLine();

            ExportParameters(ver, ver, xunderapp, headers);
            ExportParameterRefs(ver, xunderapp);
            ExportComObjects(ver, ver, xunderapp, headers);
            ExportComObjectRefs(ver, xunderapp);

            #region "Tables / LoadProcedure"
            
            temp = new XElement(Get("AddressTable"));
            if(ver.AddressMemoryObject != null && general.Info.Mask.Memory == MemoryTypes.Absolute)
            {
                temp.SetAttributeValue("CodeSegment", $"{appVersion}_AS-{ver.AddressMemoryObject.Address:X4}");
                temp.SetAttributeValue("Offset", ver.AddressTableOffset);
            }
            temp.SetAttributeValue("MaxEntries", ver.AddressTableMaxCount);
            xunderapp.Add(temp);

                temp = new XElement(Get("AssociationTable"));
            if(ver.AssociationMemoryObject != null && general.Info.Mask.Memory == MemoryTypes.Absolute)
            {
                temp.SetAttributeValue("CodeSegment", $"{appVersion}_AS-{ver.AssociationMemoryObject.Address:X4}");
                temp.SetAttributeValue("Offset", ver.AssociationTableOffset);
            }
            temp.SetAttributeValue("MaxEntries", ver.AssociationTableMaxCount);
            xunderapp.Add(temp);

            if (general.Info.Mask.Procedure != ProcedureTypes.Default)
            {
                temp = XElement.Parse(ver.Procedure);
                //Write correct Memory Size if AutoLoad is activated
                foreach (XElement xele in temp.Descendants())
                {
                    switch (xele.Name.LocalName)
                    {
                        case "LdCtrlWriteRelMem":
                            {
                                if (xele.Attribute("ObjIdx").Value == "4" && ver.Memories[0].IsAutoLoad)
                                {
                                    xele.SetAttributeValue("Size", ver.Memories[0].Size);
                                }
                                break;
                            }

                        case "LdCtrlRelSegment":
                            {
                                if (xele.Attribute("LsmIdx").Value == "4" && ver.Memories[0].IsAutoLoad)
                                {
                                    xele.SetAttributeValue("Size", ver.Memories[0].Size);
                                }
                                break;
                            }
                    }
                }
                ver.Procedure = temp.ToString();

                foreach(Message msg in ver.Messages)
                {
                    if(msg.Id == -1)
                        msg.Id = Helper.GetNextFreeId(ver, "Messages");
                }

                temp.Attributes().Where((x) => x.IsNamespaceDeclaration).Remove();
                temp.Name = XName.Get(temp.Name.LocalName, currentNamespace);
                foreach(XElement xele in temp.Descendants())
                {
                    xele.Name = XName.Get(xele.Name.LocalName, currentNamespace);
                    switch(xele.Name.LocalName)
                    {
                        case "OnError":
                        {
                            int id = -1;
                            if(!int.TryParse(xele.Attribute("MessageRef").Value, out id))
                            {
                                if(general.Application.Messages.Any(m => m.Name == xele.Attribute("MessageRef").Value))
                                    id = general.Application.Messages.Single(m => m.Name == xele.Attribute("MessageRef").Value).UId;
                            }
                            Message msg = ver.Messages.SingleOrDefault(m => m.UId == id);
                            xele.SetAttributeValue("MessageRef", $"{appVersion}_M-{msg.Id}");
                            break;
                        }

                        case "LdCtrlCompareProp":
                        {
                            if (isDevMode) {
                                xele.Remove();
                            } else {
                                if (xele.Attribute("ObjIdx").Value == "0" && xele.Attribute("PropId").Value == "12")
                                            xele.Attribute("InlineData").Value = GetManuId();
                                if(general.IsOpenKnx && xele.Attribute("ObjIdx").Value == "0" && xele.Attribute("PropId").Value == "78")
                                    xele.Attribute("InlineData").Value = $"0000{general.ManufacturerId:X2}{general.Info.AppNumber:X2}{general.Application.Number:X2}00";
                            }
                            break;
                        }
                    }
                }
                xunderapp.Add(temp);
            }
            #endregion

            xunderapp.Add(xextension);

            if(ver.IsMessagesActive && ver.Messages.Count > 0)
            {
                temp = new XElement(Get("Messages"));
                foreach(Message msg in ver.Messages)
                {
                    if(msg.Id == -1)
                        msg.Id = Helper.GetNextFreeId(ver, "Messages");

                    XElement xmsg = new XElement(Get("Message"));
                    xmsg.SetAttributeValue("Id", $"{appVersion}_M-{msg.Id}");
                    xmsg.SetAttributeValue("Name", msg.Name);
                    xmsg.SetAttributeValue("Text",  GetDefaultLanguage(msg.Text));
                    temp.Add(xmsg);

                    if(!msg.TranslationText)
                        foreach(Translation trans in msg.Text)
                            AddTranslation(trans.Language.CultureCode, $"{appVersion}_M-{msg.Id}", "Text", trans.Text);
                }
                xunderapp.Add(temp);
            }

            XElement xscript = new XElement(Get("Script"), "");
            xscript.Value = ver.Script;
            xunderapp.Add(xscript);

            #region BusInterfaces

            if(ver.IsBusInterfaceActive)
            {
                XElement xbis = new XElement(Get("BusInterfaces"));
                xunderapp.Add(xbis);

                for(int i = 1; i <= ver.BusInterfaceCounter; i++)
                {
                    XElement xbi = new XElement(Get("BusInterface"));
                    xbi.SetAttributeValue("Id", $"{appVersion}_BI-{i}");
                    xbi.SetAttributeValue("AddressIndex", i);
                    xbi.SetAttributeValue("AccessType", "Tunneling");
                    xbi.SetAttributeValue("Text", "Tunneling Channel " + i);
                    xbis.Add(xbi);
                }

                if(ver.HasBusInterfaceRouter)
                {
                    XElement xbi = new XElement(Get("BusInterface"));
                    xbi.SetAttributeValue("Id", $"{appVersion}_BI-0");
                    xbi.SetAttributeValue("AddressIndex", "0");
                    xbi.SetAttributeValue("AccessType", "Tunneling");
                    xbi.SetAttributeValue("Text", "IP Routing");
                    xbis.Add(xbi);
                }

            }
            #endregion

            #region Modules

            if(ver.Allocators.Count > 0)
            {
                XElement xallocs = new XElement(Get("Allocators"));
                xunderapp.Add(xallocs);
                
                foreach(Models.Allocator alloc in ver.Allocators)
                {
                    XElement xalloc = new XElement(Get("Allocator"));

                    if (alloc.Id == -1)
                        alloc.Id = Helper.GetNextFreeId(ver, "Allocators");
                    xalloc.SetAttributeValue("Id", $"{appVersionMod}_L-{alloc.Id}");
                    xalloc.SetAttributeValue("Name", alloc.Name);
                    xalloc.SetAttributeValue("Start", alloc.Start);
                    xalloc.SetAttributeValue("maxInclusive", alloc.Max);
                    //TODO errormessageid
                    
                    xallocs.Add(xalloc);
                }
            }

            if(ver.Modules.Count > 0)
            {
                headers.AppendLine("");
                headers.AppendLine("//---------------------Modules----------------------------");
            }

            List<DynModule> mods = new List<DynModule>();
            Helper.GetModules(ver.Dynamics[0], mods);

            if(mods.Count > 0)
            {
                headers.AppendLine("");
                headers.AppendLine("//-----Module specific starts");
            }

            Dictionary<string, (long, long)> modStartPara = new Dictionary<string, (long, long)>();
            Dictionary<string, (long, long)> modStartComs = new Dictionary<string, (long, long)>();
            Dictionary<string, long> allocators = new Dictionary<string, long>();
            foreach(DynModule dmod in mods)
            {
                string prefix = dmod.ModuleObject.Prefix;

                if(dmod.ModuleObject.IsOpenKnxModule)
                {
                    OpenKnxModule omod = ver.OpenKnxModules.Single(m => m.Name == dmod.ModuleObject.Name.Split(' ')[0]);
                    prefix = omod.Prefix + "_" + dmod.ModuleObject.Name.Split(' ')[1];
                }

                //if(!modStartPara.ContainsKey(prefix))
                //    modStartPara.Add(prefix, new List<long>());
                //if(!modStartComs.ContainsKey(prefix))
                //    modStartComs.Add(prefix, new List<long>());

                DynModuleArg dargp = dmod.Arguments.Single(a => a.ArgumentId == dmod.ModuleObject.ParameterBaseOffsetUId);
                if(dargp.UseAllocator)
                {
                    if(!allocators.ContainsKey(dargp.Allocator.Name))
                        allocators.Add(dargp.Allocator.Name, dargp.Allocator.Start);

                    if(!modStartPara.ContainsKey(prefix))
                        modStartPara.Add(prefix, (allocators[dargp.Allocator.Name], dargp.Argument.Allocates));

                    allocators[dargp.Allocator.Name] += dargp.Argument.Allocates;
                } else if(!modStartPara.ContainsKey(prefix))
                {
                    int size = (dmod.ModuleObject.Memory.Sections.Count - 1) * 16;
                    if(dmod.ModuleObject.Memory.Sections.Count > 0)
                        size += dmod.ModuleObject.Memory.Sections[dmod.ModuleObject.Memory.Sections.Count - 1].Bytes.Count;
                    modStartPara.Add(prefix, (long.Parse(dargp.Value), size));
                }
                
                DynModuleArg dargc = dmod.Arguments.SingleOrDefault(a => a.ArgumentId == dmod.ModuleObject.ComObjectBaseNumberUId);
                if(dargc != null)
                {
                    if(dargc.UseAllocator)
                    {
                        if(!allocators.ContainsKey(dargc.Allocator.Name))
                            allocators.Add(dargc.Allocator.Name, dargc.Allocator.Start);

                        if(!modStartComs.ContainsKey(prefix))
                            modStartComs.Add(prefix, (allocators[dargc.Allocator.Name], dargc.Argument.Allocates));

                        allocators[dargc.Allocator.Name] += dargc.Argument.Allocates;
                    } else if(!modStartComs.ContainsKey(prefix))
                    {
                        long size = 0;
                        if(dmod.ModuleObject.ComObjects.Count > 0)
                        {
                            ComObject com = dmod.ModuleObject.ComObjects.OrderByDescending(c => c.Number).First();
                            size = com.Number + 1;
                        }
                        modStartComs.Add(prefix, (long.Parse(dargc.Value), size));
                    }
                }
            }

            foreach(KeyValuePair<string, (long offset,long size)> item in modStartPara)
            {
                headers.AppendLine($"#define {item.Key}_ParamBlockOffset " + item.Value.offset);
                if(item.Value.size > 0)
                    headers.AppendLine($"#define {item.Key}_ParamBlockSize " + item.Value.size);
                else
                    headers.AppendLine($"#define {item.Key}_ParamBlockSize 0");
            }
            foreach(KeyValuePair<string, (long offset, long size)> item in modStartComs)
            {
                headers.AppendLine($"#define {item.Key}_KoOffset " + item.Value.offset);
                if(item.Value.size > 0)
                    headers.AppendLine($"#define {item.Key}_KoBlockSize " + item.Value.size);
                else
                    headers.AppendLine($"#define {item.Key}_KoBlockSize 0");
            }

            headers.AppendLine();

            ExportModules(xapp, ver, ver, appVersion, headers, appVersion);
            appVersionMod = appVersion;

            string headersString = headers.ToString();
            if(general.ManufacturerId == 0x02DC)
            {
                headersString = headersString.Replace("knx.paramBit", "knx_memory_param_bit");
                headersString = headersString.Replace("knx.paramByte", "knx_memory_param_byte");
                headersString = headersString.Replace("knx.paramFloat", "knx_memory_param_float");
                headersString = headersString.Replace("knx.getGroupObject", "");
            }
            System.IO.File.WriteAllText(headerPath, headersString);
            headers = null;
            #endregion

            XElement xdyn = new XElement(Get("Dynamic"));
            GenerateModuleCounter(ver);
            HandleSubItems(ver.Dynamics[0], xdyn, ver, ver);

            if(buttonScripts.Count > 0)
            {
                string scripts = "";
                scripts += string.Join(null, buttonScripts);
                xscript.Value += scripts;
            }

            if(string.IsNullOrEmpty(xscript.Value))
                xscript.Remove();

            if(iconsApp.Count > 0)
            {
                string zipName = "Icons_" + general.GetGuid();
                Baggage bag = new Baggage() {
                    Name = zipName,
                    Extension = ".zip",
                    LastModified = general.Icons.OrderByDescending(i => i.LastModified).First().LastModified
                };
                baggagesManu.Add(bag);
                if(ver.NamespaceVersion == 14)
                {
                    xapp.SetAttributeValue("IconFile", $"{zipName}.zip");
                } else {
                    xapp.SetAttributeValue("IconFile", $"{Manu}_BG--{GetEncoded($"{zipName}.zip")}");
                }

                XElement xbag = new XElement(Get("Baggage"));
                xbag.SetAttributeValue("RefId", $"M-{GetManuId()}_BG--{GetEncoded($"{zipName}.zip")}");
                xextension.Add(xbag);
                exportIcons = true;
            }
            
            if(!xextension.HasElements)
                xextension.Remove();

            xapp.Add(xdyn);

            #region Translations
            Log($"Exportiere Translations: {languages.Count} Sprachen");
            xlanguages = new XElement(Get("Languages"));
            foreach(KeyValuePair<string, Dictionary<string, Dictionary<string, string>>> lang in languages) {
                XElement xunit = new XElement(Get("TranslationUnit"));
                xunit.SetAttributeValue("RefId", appVersion);
                XElement xlang = new XElement(Get("Language"), xunit);
                xlang.SetAttributeValue("Identifier", lang.Key);

                foreach(KeyValuePair<string, Dictionary<string, string>> langitem in lang.Value) {
                    XElement xele = new XElement(Get("TranslationElement"));
                    xele.SetAttributeValue("RefId", langitem.Key);

                    foreach(KeyValuePair<string, string> langval in langitem.Value) {
                        XElement xtrans = new XElement(Get("Translation"));
                        xtrans.SetAttributeValue("AttributeName", langval.Key);
                        xtrans.SetAttributeValue("Text", langval.Value);
                        xele.Add(xtrans);
                    }

                    if(xele.HasElements)
                        xunit.Add(xele);
                }
                if(xlang.HasElements)
                    xlanguages.Add(xlang);
            }
            xmanu.Add(xlanguages);
            #endregion

            string xsdFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "knx_project_" + general.Application.NamespaceVersion + ".xsd");
            if (File.Exists(xsdFile))
            {
                doc.Save(GetRelPath("Temp", Manu, appVersion + ".validate.xml"));
                Log("XSD gefunden. Validierung wird ausgeführt");
                XmlSchemaSet schemas = new XmlSchemaSet();
                schemas.Add(null, xsdFile);
                bool flag = false;

                XDocument doc2 = XDocument.Load(GetRelPath("Temp", Manu, appVersion + ".validate.xml"), LoadOptions.SetLineInfo);

                doc2.Validate(schemas, (o, e) => {
                    LogE($"Fehler beim Validieren! Zeile {e.Exception.LineNumber}:{e.Exception.LinePosition}\r\n--->{e.Message}\r\n--->({o})");
                    flag = true;
                });

                if(!flag)
                    File.Delete(GetRelPath("Temp", Manu, appVersion + ".validate.xml"));

                if(flag)
                {
                    return false;
                }
            }
            
            doc.Root.Attributes().Where((x) => x.IsNamespaceDeclaration).Remove();
            doc.Root.Name = doc.Root.Name.LocalName;
            foreach(XElement xele in doc.Descendants())
            {
                xele.Name = xele.Name.LocalName;
            }

            Log($"Speichere App: {GetRelPath("Temp", Manu, appVersion + ".xml")}");
            doc.Save(GetRelPath("Temp", Manu, appVersion + ".xml"));
            Log("Speichern beendet");
            #endregion

            #region XML Hardware
            languages.Clear();
            Log("Exportiere Hardware");
            xmanu = CreateNewXML(Manu);
            XElement xhards = new XElement(Get("Hardware"));
            xmanu.Add(xhards);
            
            string hid = Manu + "_H-" + GetEncoded(general.Info.SerialNumber) + "-" + general.Info.Version;
            XElement xhard = new XElement(Get("Hardware"));
            xhard.SetAttributeValue("Id", hid);
            xhard.SetAttributeValue("Name", general.Info.Name);
            xhard.SetAttributeValue("SerialNumber", general.Info.SerialNumber);
            xhard.SetAttributeValue("VersionNumber", general.Info.Version.ToString());
            xhard.SetAttributeValue("BusCurrent", general.Info.BusCurrent);
            if (general.Info.HasIndividualAddress) xhard.SetAttributeValue("HasIndividualAddress", "1");
            if (general.Info.HasApplicationProgram) xhard.SetAttributeValue("HasApplicationProgram", "1");
            if (general.Info.HasApplicationProgram2) xhard.SetAttributeValue("HasApplicationProgram2", "1");
            if (general.Info.IsPowerSupply) xhard.SetAttributeValue("IsPowerSupply", "1");
            if (general.Info.IsCoppler) xhard.SetAttributeValue("IsCoupler", "1");
            if (general.Info.IsPowerSupply) xhard.SetAttributeValue("IsPowerSupply", "1");
            //xhard.SetAttributeValue("IsCable", "0"); //Todo check if means PoweLine Cable
            //xhard.SetAttributeValue("IsChoke", "0"); //Ist immer 0 da keine Drossel
            //xhard.SetAttributeValue("IsPowerLineRepeater", "0");
            //xhard.SetAttributeValue("IsPowerLineSignalFilter", "0");
            if (general.Info.IsIpEnabled) xhard.SetAttributeValue("IsIPEnabled", "1");

            XElement xprods = new XElement(Get("Products"));
            xhard.Add(xprods);
            XElement xprod = new XElement(Get("Product"));
            string pid = hid + "_P-" + GetEncoded(general.Info.OrderNumber);
            ProductIds.Add(general.Info.Name, pid);
            xprod.SetAttributeValue("Id", pid);
            xprod.SetAttributeValue("Text", GetDefaultLanguage(general.Info.Text));
            xprod.SetAttributeValue("OrderNumber", general.Info.OrderNumber);
            xprod.SetAttributeValue("IsRailMounted", general.Info.IsRailMounted ? "1" : "0");
            xprod.SetAttributeValue("DefaultLanguage", currentLang);
            xprod.Add(new XElement(Get("RegistrationInfo"), new XAttribute("RegistrationStatus", "Registered")));
            xprods.Add(xprod);

            foreach(Models.Translation trans in general.Info.Text) AddTranslation(trans.Language.CultureCode, pid, "Text", trans.Text);

            XElement xasso = new XElement(Get("Hardware2Programs"));
            xhard.Add(xasso);

            string appidx = GetAppId(general.Info.AppNumber) + "-" + ver.Number.ToString("X2") + "-0000";

            XElement xh2p = new XElement(Get("Hardware2Program"));
            xh2p.SetAttributeValue("Id", hid + "_HP-" + appidx);
            xh2p.SetAttributeValue("MediumTypes", general.Info.Mask.MediumTypes);

            HardwareIds.Add(general.Info.Version + "-" + GetAppId(general.Info.AppNumber) + "-" + ver.Number, hid + "_HP-" + appidx);

            xh2p.Add(new XElement(Get("ApplicationProgramRef"), new XAttribute("RefId", Manu + "_A-" + appidx)));

            XElement xreginfo = new XElement(Get("RegistrationInfo"));
            xreginfo.SetAttributeValue("RegistrationStatus", "Registered");
            xreginfo.SetAttributeValue("RegistrationNumber", "0001/" + general.Info.Version + ver.Number);
            xh2p.Add(xreginfo);
            xasso.Add(xh2p);
            xhards.Add(xhard);

            Log($"Exportiere Translations: {languages.Count} Sprachen");
            xlanguages = new XElement(Get("Languages"));
            foreach(KeyValuePair<string, Dictionary<string, Dictionary<string, string>>> lang in languages) {
                XElement xlang = new XElement(Get("Language"));
                xlang.SetAttributeValue("Identifier", lang.Key);

                foreach(KeyValuePair<string, Dictionary<string, string>> langitem in lang.Value) {
                    XElement xunit = new XElement(Get("TranslationUnit"));
                    xunit.SetAttributeValue("RefId", langitem.Key);
                    xlang.Add(xunit);
                    XElement xele = new XElement(Get("TranslationElement"));
                    xele.SetAttributeValue("RefId", langitem.Key);

                    foreach(KeyValuePair<string, string> langval in langitem.Value) {
                        XElement xtrans = new XElement(Get("Translation"));
                        xtrans.SetAttributeValue("AttributeName", langval.Key);
                        xtrans.SetAttributeValue("Text", langval.Value);
                        xele.Add(xtrans);
                    }

                    xunit.Add(xele);
                }
                xlanguages.Add(xlang);
            }
            xmanu.Add(xlanguages);

            Log($"Speichere Hardware: {GetRelPath("Temp", Manu, "Hardware.xml")}");
            doc.Root.Attributes().Where((x) => x.IsNamespaceDeclaration).Remove();
            doc.Root.Name = doc.Root.Name.LocalName;
            foreach(XElement xele in doc.Descendants())
            {
                xele.Name = xele.Name.LocalName;
            }
            doc.Save(GetRelPath("Temp", Manu, "Hardware.xml"));
            #endregion

            #region XML Catalog
            Log("Exportiere Catalog");
            languages.Clear();
            xmanu = CreateNewXML(Manu);
            XElement cat = new XElement(Get("Catalog"));

            foreach (CatalogItem item in general.Catalog[0].Items)
            {
                GetCatalogItems(item, cat, ProductIds, HardwareIds);
            }
            xmanu.Add(cat);

            Log($"Exportiere Translations: {languages.Count} Sprachen");
            xlanguages = new XElement(Get("Languages"));
            foreach(KeyValuePair<string, Dictionary<string, Dictionary<string, string>>> lang in languages) {
                XElement xlang = new XElement(Get("Language"));
                xlang.SetAttributeValue("Identifier", lang.Key);

                foreach(KeyValuePair<string, Dictionary<string, string>> langitem in lang.Value) {
                    XElement xunit = new XElement(Get("TranslationUnit"));
                    xunit.SetAttributeValue("RefId", langitem.Key);
                    xlang.Add(xunit);

                    XElement xele = new XElement(Get("TranslationElement"));
                    xele.SetAttributeValue("RefId", langitem.Key);

                    foreach(KeyValuePair<string, string> langval in langitem.Value) {
                        XElement xtrans = new XElement(Get("Translation"));
                        xtrans.SetAttributeValue("AttributeName", langval.Key);
                        xtrans.SetAttributeValue("Text", langval.Value);
                        xele.Add(xtrans);
                    }

                    xunit.Add(xele);
                }
                xlanguages.Add(xlang);
            }
            xmanu.Add(xlanguages);

            Log($"Speichere Catalog: {GetRelPath("Temp", Manu, "Catalog.xml")}");
            doc.Root.Attributes().Where((x) => x.IsNamespaceDeclaration).Remove();
            doc.Root.Name = doc.Root.Name.LocalName;
            foreach(XElement xele in doc.Descendants())
            {
                xele.Name = xele.Name.LocalName;
            }
            doc.Save(GetRelPath("Temp", Manu, "Catalog.xml"));
            #endregion
        
            #region XML Baggages/Icons
            if(baggagesManu.Count > 0)
            {
                Log("Exportiere Baggages");
                languages.Clear();
                xmanu = CreateNewXML(Manu);
                XElement xbags = new XElement(Get("Baggages"));

                foreach (Baggage bag in baggagesManu)
                {
                    XElement xbag = new XElement(Get("Baggage"));
                    xbag.SetAttributeValue("TargetPath", bag.TargetPath);
                    xbag.SetAttributeValue("Name", bag.Name + bag.Extension);
                    xbag.SetAttributeValue("Id", $"M-{GetManuId()}_BG-{GetEncoded(bag.TargetPath)}-{GetEncoded(bag.Name + bag.Extension)}");

                    XElement xinfo = new XElement(Get("FileInfo"));
                    string time = bag.LastModified.ToUniversalTime().ToString("O");
                    xinfo.SetAttributeValue("TimeInfo", time);
                    xbag.Add(xinfo);

                    xbags.Add(xbag);

                    if (!Directory.Exists(GetRelPath("Temp", Manu, "Baggages", bag.TargetPath)))
                        Directory.CreateDirectory(GetRelPath("Temp", Manu, "Baggages", bag.TargetPath));

                    if(bag.Data != null)
                    {
                        File.WriteAllBytes(GetRelPath("Temp", Manu, "Baggages", bag.TargetPath, bag.Name + bag.Extension), bag.Data);
                        File.SetLastWriteTime(GetRelPath("Temp", Manu, "Baggages", bag.TargetPath, bag.Name + bag.Extension), bag.LastModified);
                    }
                }

                xmanu.Add(xbags);
                doc.Save(GetRelPath("Temp", Manu, "Baggages.xml"));
            } else
            {
                Log("Exportiere keine Baggages");
            }

            if(exportIcons)
            {
                Log("Exportiere Icons");
                string zipName = "Icons_" + general.GetGuid() + ".zip";
                using (var stream = new FileStream(GetRelPath("Temp", Manu, "Baggages", zipName), FileMode.Create))
                    using (var archive = new ZipArchive(stream , ZipArchiveMode.Create, false,  System.Text.Encoding.GetEncoding(850)))
                    {
                        foreach(Icon icon in general.Icons)
                        {
                            ZipArchiveEntry entry = archive.CreateEntry(icon.Name + ".png");
                            using(Stream s = entry.Open())
                            {
                                s.Write(icon.Data, 0, icon.Data.Length);
                            }
                        }
                    }

                DateTime last = general.Icons.OrderByDescending(i => i.LastModified).First().LastModified;
                File.SetLastWriteTime(GetRelPath("Temp", Manu, "Baggages", zipName), last);
            }
            #endregion

            Log("Export beendet");
            return true;
        }

        private Dictionary<string, int> moduleCounter = new Dictionary<string, int>();
        private void GenerateModuleCounter(IVersionBase ver)
        {
            List<DynModule> modules = new List<DynModule>();
            Helper.GetModules(ver.Dynamics[0], modules);

            string name = "base";
            if(ver is Models.Module mod)
                name = mod.Name;

            foreach(DynModule dmod in modules)
            {
                string key = name + dmod.ModuleObject.Name;
                if(moduleCounter.ContainsKey(key))
                {
                    if(moduleCounter[key] < dmod.Id)
                        moduleCounter[key] = dmod.Id;
                } else {
                    moduleCounter.Add(key, dmod.Id);
                }
            }
        }

        public static string HeaderNameEscape(string name)
        {
            return name.Replace(' ', '_').Replace('-', '_');
        }

        private void ExportModules(XElement xparent, AppVersion ver, IVersionBase parent, string modVersion, StringBuilder headers, string moduleName, int depth = 0)
        {
            if(parent.Modules.Count > 0)
            {
                string subName = depth == 0 ? "ModuleDefs" : "SubModuleDefs";
                XElement xunderapp = new XElement(Get(subName));
                xparent.Add(xunderapp);

                int counter = 0;

                List<DynModule> mods = new List<DynModule>();
                Helper.GetModules(parent.Dynamics[0], mods);

                foreach (Models.Module mod in parent.Modules)
                {
                    Log($"---Modul: {mod.Name}");
                    if(!mods.Any(m => m.ModuleUId == mod.UId))
                    {
                        Log($"Skipping {mod.Name} since it is not used in Dynamic");
                        continue;
                    }
                    counter++;
                    mod.Id = counter;
                    headers.AppendLine("//-----Module: " + mod.Name);
                    //if (mod.Id == -1)
                    //    mod.Id = AutoHelper.GetNextFreeId(vers, "Modules");

                    if(mod.IsOpenKnxModule && mod.Name.EndsWith("Templ"))
                    {
                        string oname = mod.Name.Substring(0, mod.Name.IndexOf(' '));
                        OpenKnxModule omod = ver.OpenKnxModules.Single(o => o.Name == oname);
                        int count = mods.Count(m => m.ModuleUId == mod.UId);
                        headers.AppendLine($"#define {omod.Prefix}_ChannelCount {count}");
                    }

                    XElement temp = new XElement(Get("Arguments"));
                    XElement xmod = new XElement(Get("ModuleDef"), temp);
                    xmod.SetAttributeValue("Name", mod.Name);

                    appVersionMod = $"{modVersion}_{(depth == 0 ? "MD" : "SM")}-{mod.Id}";
                    string newModVersion = appVersionMod;
                    xmod.SetAttributeValue("Id", $"{appVersionMod}");
                    mod.ExportHelper = appVersionMod;

                    foreach (Models.Argument arg in mod.Arguments)
                    {
                        XElement xarg = new XElement(Get("Argument"));
                        if (arg.Id == -1)
                            arg.Id = Helper.GetNextFreeId(mod, "Arguments");
                        arg.ExportHelper = $"{mod.ExportHelper}_A-{arg.Id}";
                        xarg.SetAttributeValue("Id", arg.ExportHelper);
                        xarg.SetAttributeValue("Name", arg.Name);
                        if(arg.Type == ArgumentTypes.Text)
                        {
                            xarg.SetAttributeValue("Type", "Text");
                        } else
                        {
                            xarg.SetAttributeValue("Allocates", arg.Allocates);
                        }
                        temp.Add(xarg);
                    }
                    XElement xunderstatic = new XElement(Get("Static"));
                    xmod.Add(xunderstatic);
                    xunderapp.Add(xmod);

                    ExportParameters(ver, mod, xunderstatic, headers);
                    ExportParameterRefs(mod, xunderstatic);
                    ExportComObjects(ver, mod, xunderstatic, headers);
                    ExportComObjectRefs(mod, xunderstatic);

                    if(mod.Allocators.Count > 0)
                    {
                        XElement xallocs = new XElement(Get("Allocators"));
                        xunderstatic.Add(xallocs);
                        
                        foreach(Models.Allocator alloc in mod.Allocators)
                        {
                            XElement xalloc = new XElement(Get("Allocator"));

                            if (alloc.Id == -1)
                                alloc.Id = Helper.GetNextFreeId(mod, "Allocators");
                            xalloc.SetAttributeValue("Id", $"{appVersionMod}_L-{alloc.Id}");
                            xalloc.SetAttributeValue("Name", alloc.Name);
                            xalloc.SetAttributeValue("Start", alloc.Start);
                            xalloc.SetAttributeValue("maxInclusive", alloc.Max);
                            //TODO errormessageid
                            
                            xallocs.Add(xalloc);
                        }
                    }
                    ExportModules(xmod, ver, mod, appVersionMod, headers, newModVersion, depth + 1);

                    appVersionMod = $"{modVersion}_{(depth == 0 ? "MD" : "SM")}-{mod.Id}";
                    
                    XElement xmoddyn = new XElement(Get("Dynamic"));
                    xmod.Add(xmoddyn);
                    GenerateModuleCounter(mod);
                    HandleSubItems(mod.Dynamics[0], xmoddyn, ver, mod);

                    headers.AppendLine("");

                    appVersionMod = modVersion;
                    Log("---");
                }
            }
        }

        private void ExportHelptexts(AppVersion ver, string manu, List<Baggage> baggagesManu, List<Baggage> baggagesApp)
        {
            if(ver.Helptexts.Count == 0) return;
            if(System.IO.Directory.Exists(GetRelPath("HelpTemp")))
                System.IO.Directory.Delete(GetRelPath("HelpTemp"), true);
            System.IO.Directory.CreateDirectory(GetRelPath("HelpTemp"));

            foreach(Language lang in ver.Languages)
            {
                if(!System.IO.Directory.Exists(GetRelPath("HelpTemp", lang.CultureCode)))
                    System.IO.Directory.CreateDirectory(GetRelPath("HelpTemp", lang.CultureCode));
            }

            foreach(Helptext text in ver.Helptexts)
            {
                foreach(Translation trans in text.Text)
                {
                    System.IO.File.WriteAllText(GetRelPath("HelpTemp", trans.Language.CultureCode, text.Name + ".txt"), trans.Text);
                }
            }

            if(!System.IO.Directory.Exists(GetRelPath("Temp", manu, "Baggages")))
                System.IO.Directory.CreateDirectory(GetRelPath("Temp", manu, "Baggages"));
            
            foreach(Language lang in ver.Languages)
            {
                string destPath = GetRelPath("Temp", manu, "Baggages", "HelpFile_" + lang.CultureCode + ".zip");
                if(File.Exists(destPath))
                    File.Delete(destPath);
                System.IO.Compression.ZipFile.CreateFromDirectory(GetRelPath("HelpTemp", lang.CultureCode), destPath);
                Baggage bag = new Baggage() {
                    Name = "HelpFile_" + lang.CultureCode,
                    Extension = ".zip",
                    LastModified = DateTime.Now
                };
                if(!baggagesManu.Contains(bag))
                    baggagesManu.Add(bag);
                baggagesApp.Add(bag);
                if(ver.NamespaceVersion == 14)
                    AddTranslation(lang.CultureCode, appVersion, "ContextHelpFile", "HelpFile_" + lang.CultureCode + ".zip");
                else
                    AddTranslation(lang.CultureCode, appVersion, "ContextHelpFile", $"{manu}_BG--{GetEncoded("HelpFile_" + lang.CultureCode + ".zip")}");
            }

            System.IO.Directory.Delete(GetRelPath("HelpTemp"), true);
        }

        private void ExportSegments(AppVersion ver, XElement xparent)
        {
            Log($"Exportiere Segmente: {ver.Memories.Count}x");
            XElement codes = new XElement(Get("Code"));
            foreach (Memory mem in ver.Memories)
            {
                if(mem.IsAutoPara)
                    MemoryHelper.MemoryCalculation(general, mem);
                    
                XElement xmem = null;
                string id = "";
                switch (mem.Type)
                {
                    case MemoryTypes.Absolute:
                        xmem = new XElement(Get("AbsoluteSegment"));
                        id = $"{appVersion}_AS-{mem.Address:X4}";
                        xmem.SetAttributeValue("Id", id);
                        xmem.SetAttributeValue("Address", mem.Address);
                        xmem.SetAttributeValue("Size", mem.Size);
                        if(ver.ComObjectMemoryObject == mem){

                            
                            byte[] groupObjectTableData = CalculateGroupObjectTable(ver);
                            byte[] segmentData = new byte[ver.ComObjectMemoryObject.Size];
                            for(int i = 0; i < ver.ComObjectTableOffset; i++) {
                                segmentData[i] = 0;
                            }
                            // check if the GroupObjectTable at Offset will fit into the data section
                            if(ver.ComObjectTableOffset + groupObjectTableData.Length <= ver.ComObjectMemoryObject.Size) {
                                groupObjectTableData.CopyTo(segmentData, ver.ComObjectTableOffset);
                                if(general.Info.Mask.ManagementModel == "Bcu1"){
                                    // if the ManagementModel is BCU1 the first Byte has to contain the size of the data section
                                    segmentData[0] = (byte)(ver.ComObjectMemoryObject.Size - 1); 
                                    // the next data bytes will be transferred from ETS to the specified addresses in the BCU1
                                    //segmentData[14]  // Byte 0x010E of the BCU1 (Routing-count constant)
                                    //segmentData[15]  // Byte 0x010F of the BCU1 (INAK-Retransmit-Limit | BUSY-Retransmit-Limit)
                                    //segmentData[16]  // Byte 0x0110 of the BCU1 (Configuration Descriptor)
                                    //segmentData[17]  // Byte 0x0111 of the BCU1 (Pointer to Association Table) -> is calculated automatically by ETS 
                                    segmentData[18] = (byte)ver.ComObjectTableOffset; // Byte 0x0112 of the BCU1 (Pointer to Communication Object Table)
                                    //segmentData[19]  // Byte 0x0113 of the BCU1 (Pointer to USER Initialization Routine)
                                    //segmentData[20]  // Byte 0x0114 of the BCU1 (Pointer to USER Program)
                                    //segmentData[21]  // Byte 0x0115 of the BCU1 (Pointer to USER Save Program)
                                }
                            }
                            xmem.Add(new XElement(Get("Data"), System.Convert.ToBase64String(segmentData)));
                        }
                        break;

                    case MemoryTypes.Relative:
                        xmem = new XElement(Get("RelativeSegment"));
                        id = $"{appVersion}_RS-04-{mem.Offset:X5}";
                        xmem.SetAttributeValue("Id", id);
                        xmem.SetAttributeValue("Name", mem.Name);
                        xmem.SetAttributeValue("Offset", mem.Offset);
                        xmem.SetAttributeValue("Size", mem.Size);
                        xmem.SetAttributeValue("LoadStateMachine", "4");
                        break;
                }

                if (xmem == null) continue;
                codes.Add(xmem);
            }
            xparent.Add(codes);
        }

        private byte[] CalculateGroupObjectTable(AppVersion ver)
        {
            uint dataPtrSize;
            if(general.Info.Mask.ManagementModel == "Bcu1")
            {
                dataPtrSize = 1;
            }
            else //BCU2, BIM112
            {
                dataPtrSize = 2;
            }

            byte[] data = new byte[1 + dataPtrSize];

            int ramPointer = 0;

            // den Start des User RAM Bereichs festlegen (aus der sblib übernommen)
            if (general.Info.Mask.ManagementModel == "Bcu1" ||
                general.Info.Mask.ManagementModel == "Bcu2")
            {
                ramPointer = 0x00; // USER_RAM_START_DEFAULT bei BCU1, BCU2 = 0
            }
            else // BIM112
            {
                ramPointer = 0x5FC; // USER_RAM_START_DEFAULT bei BIM112 = 0x5FC
            }

            // wir legen die RAM-Flags-Table an den Anfang des User-RAMs
            if (general.Info.Mask.ManagementModel == "Bcu1")
            {
                data[1] = (byte)ramPointer; // RAM-Flags-Table Pointer
            }
            else //BCU2, BIM112
            {
                data[1] = (byte)(ramPointer >> 8); // RAM-Flags-Table Pointer
                data[2] = (byte)ramPointer; // RAM-Flags-Table Pointer
            }

            // Die höchste Nummer der Kommunikationsobjekte holen (Die Anzal der Tabelleneinträge ist gleich der höchsten Nummer)
            // da das erste Kommunikationsobjekt die Nummer 0 hat, muss +1 addiert werden
            int numberOfComObjects = 0;
            foreach(ComObject comObject in ver.ComObjects){
                if(comObject.Number > numberOfComObjects){
                    numberOfComObjects = comObject.Number;
                }
            }
            numberOfComObjects += 1;
            
            // den RAM Pointer um die Anzahl der Kommunikationsobjekte erhöhen, da die Flags-Table pro Kommunikationsobjekt ein Byte Platz benötigt
            ramPointer += numberOfComObjects;

            // die Größe der Tabelle steht im ersten Byte (Anzahl der ComObjekte)
            data[0] = (byte)(numberOfComObjects); 

            for (uint comObjectNumberCounter = 0; comObjectNumberCounter < numberOfComObjects; comObjectNumberCounter++)
            {
                ComObject comObject = ver.ComObjects.ToList().Find(x => x.Number == comObjectNumberCounter);
                if (comObject != null)
                {
                    byte configByte = CreateConfigByte(comObject, general.Info.Mask.ManagementModel);
                    byte typeByte = CreateTypeByte(comObject);

                    // Berechnen der Stelle des GroupObject Descriptors im Data Segment
                    uint descriptorPointer = (comObjectNumberCounter * (dataPtrSize + 2)) + dataPtrSize + 1;

                    if (dataPtrSize == 1) // BCU1
                    {
                        Array.Resize(ref data, (int)descriptorPointer + 3);
                        data[descriptorPointer] = (byte)ramPointer;
                        data[descriptorPointer + 1] = configByte;
                        data[descriptorPointer + 2] = typeByte;
                    }
                    else // BCU2, BIM112
                    {
                        Array.Resize(ref data, (int)descriptorPointer + 4);
                        data[descriptorPointer] = (byte)(ramPointer >> 8);
                        data[descriptorPointer + 1] = (byte)ramPointer;
                        data[descriptorPointer + 2] = configByte;
                        data[descriptorPointer + 3] = typeByte;
                    }

                    if(comObject.ObjectSize < 8){
                        ramPointer += 1;
                    }else{
                        byte amountBytes = (byte)(comObject.ObjectSize / 8);
                        ramPointer += amountBytes;
                    }
                }
            }
            return data;
        }

         private static byte CreateConfigByte(ComObject comObject, string managementModel)
        {
            byte configByte = 0;

            if(managementModel == "Bcu1"){
                configByte |= (1 << 7); // bei den Masken 0x0010, 0x0011 und 0x0012 ist das 7. Bit immer 1
            }else if(comObject.FlagUpdate)
            {
                configByte |= (1 << 7);
            }

            if (comObject.FlagTrans)
                configByte |= (1 << 6);

            if (comObject.FlagWrite)
                configByte |= (1 << 4);

            if (comObject.FlagRead)
                configByte |= (1 << 3);

            if (comObject.FlagComm)
                configByte |= (1 << 2);

            // Bit 0 und 1 im config Byte geben die Priorität an
            configByte |= (byte)0x3; // low priority

            return configByte;
        }

        private static byte CreateTypeByte(ComObject comObject)
        {
            byte typeByte = 0;

            if(comObject.ObjectSize < 8){
                typeByte = (byte)(comObject.ObjectSize - 1);
            }else{
                byte amountBytes = (byte)(comObject.ObjectSize / 8);
                typeByte = (byte)(amountBytes + 6);
            }

            return typeByte;
        }

        private void ExportParameters(AppVersion ver, IVersionBase vbase, XElement xparent, StringBuilder headers)
        {
            Log($"Exportiere Parameter: {vbase.Parameters.Count}x");
            if(vbase.Parameters.Count == 0) return;
            XElement xparas = new XElement(Get("Parameters"));

            foreach (Parameter para in vbase.Parameters.Where(p => !p.IsInUnion))
            {
                //Log($"    - Parameter {para.UId} {para.Name}");
                ParseParameter(para, xparas, ver, vbase, headers);
            }

            Log($"Exportiere Unions: {vbase.Parameters.Where(p => p.IsInUnion).GroupBy(p => p.UnionObject).Count()}x");
            foreach (var paras in vbase.Parameters.Where(p => p.IsInUnion).GroupBy(p => p.UnionObject))
            {
                XElement xunion = new XElement(Get("Union"));
                xunion.SetAttributeValue("SizeInBit", paras.Key.SizeInBit);

                switch (paras.Key.SavePath)
                {
                    case SavePaths.Memory:
                        XElement xmem = new XElement(Get("Memory"));
                        string memid = $"{appVersion}_";
                        if (paras.Key.MemoryObject.Type == MemoryTypes.Absolute)
                            memid += $"AS-{paras.Key.MemoryObject.Address:X4}";
                        else
                            memid += $"RS-04-{paras.Key.MemoryObject.Offset:X5}";
                        xmem.SetAttributeValue("CodeSegment", memid);
                        xmem.SetAttributeValue("Offset", paras.Key.Offset);
                        xmem.SetAttributeValue("BitOffset", paras.Key.OffsetBit);
                        if(vbase is Models.Module mod)
                        {
                            xmem.SetAttributeValue("BaseOffset", $"{appVersionMod}_A-{mod.ParameterBaseOffset.Id}");
                        }
                        xunion.Add(xmem);
                        break;

                    default:
                        throw new Exception("Not supportet SavePath for Union (" + paras.Key.Name + ")!");
                }

                foreach (Parameter para in paras)
                {
                    //Log($"        - Parameter {para.UId} {para.Name}");
                    ParseParameter(para, xunion, ver, vbase, headers);
                }

                xparas.Add(xunion);
            }
            
            xparent.Add(xparas);
        }

        private void ExportParameterRefs(IVersionBase vbase, XElement xparent)
        {
            Log($"Exportiere ParameterRefs: {vbase.ParameterRefs.Count}x");
            if(vbase.ParameterRefs.Count == 0) return;
            XElement xrefs = new XElement(Get("ParameterRefs"));

            foreach (ParameterRef pref in vbase.ParameterRefs)
            {
                //Log($"    - ParameterRef {pref.UId} {pref.Name}");
                if (pref.ParameterObject == null) continue;
                XElement xpref = new XElement(Get("ParameterRef"));
                string id = appVersionMod + (pref.ParameterObject.IsInUnion ? "_UP-" : "_P-") + pref.ParameterObject.Id;
                xpref.SetAttributeValue("Id", $"{id}_R-{pref.Id}");
                xpref.SetAttributeValue("RefId", id);
                id += $"_R-{pref.Id}";
                xpref.SetAttributeValue("Id", id);
                if(!string.IsNullOrEmpty(pref.Name))
                    xpref.SetAttributeValue("Name", pref.Name);
                if(pref.DisplayOrder != -1)
                    xpref.SetAttributeValue("DisplayOrder", pref.DisplayOrder);

                if(pref.OverwriteAccess && pref.Access != ParamAccess.ReadWrite)
                    xpref.SetAttributeValue("Access", pref.Access.ToString());
                if (pref.OverwriteValue)
                    xpref.SetAttributeValue("Value", pref.Value);
                if(pref.OverwriteText)
                {
                    xpref.SetAttributeValue("Text", GetDefaultLanguage(pref.Text));
                    if(!pref.TranslationText)
                        foreach(Models.Translation trans in pref.Text) AddTranslation(trans.Language.CultureCode, id, "SuffixText", trans.Text);
                }
                if(pref.OverwriteSuffix)
                {
                    xpref.SetAttributeValue("SuffixText", pref.Suffix.Single(p => p.Language.CultureCode == currentLang).Text);
                    if(!pref.TranslationSuffix)
                        foreach(Models.Translation trans in pref.Suffix) AddTranslation(trans.Language.CultureCode, id, "SuffixText", trans.Text);
                }
                xrefs.Add(xpref);
            }

            xparent.Add(xrefs);
        }

        private void ExportComObjects(AppVersion ver, IVersionBase vbase, XElement xparent, StringBuilder headers)
        {
            Log($"Exportiere ComObjects: {vbase.ComObjects.Count}x");
            XElement xcoms;
            if(vbase is Models.AppVersion)
                xcoms = new XElement(Get("ComObjectTable"));
            else
                xcoms = new XElement(Get("ComObjects"));

            Models.Argument baseNumber = null;
            if(vbase is Models.Module mod)
            {
                baseNumber = mod.ComObjectBaseNumber;
            }
            if(vbase is Models.AppVersion aver)
            {
                if(aver.ComObjectMemoryObject != null && aver.ComObjectMemoryObject.Type == MemoryTypes.Absolute)
                {
                    xcoms.SetAttributeValue("CodeSegment", $"{appVersion}_AS-{aver.ComObjectMemoryObject.Address:X4}");
                    xcoms.SetAttributeValue("Offset", aver.ComObjectTableOffset);
                }
            }

            foreach (ComObject com in vbase.ComObjects)
            {
                //Log($"    - ComObject {com.UId} {com.Name}");
                if(headers != null)
                {
                    string line;
                    headers.AppendLine($"//!< Number: {com.Number}, Text: {GetDefaultLanguage(com.Text)}, Function: {GetDefaultLanguage(com.FunctionText)}");
                    if(vbase is Models.Module vmod)
                    {
                        string prefix = vmod.Prefix;
                        if(vmod.IsOpenKnxModule)
                        {
                            OpenKnxModule omod = ver.OpenKnxModules.Single(m => m.Name == vmod.Name.Split(' ')[0]);
                            prefix = omod.Prefix;
                        }

                        line = $"#define Ko{prefix}_{HeaderNameEscape(com.Name)}";
                        string definel = $"{prefix}_Ko{HeaderNameEscape(com.Name)}";

                        if(vmod.IsOpenKnxModule)
                        {
                            prefix += "_" + vmod.Name.Substring(vmod.Name.IndexOf(' ')+1);
                        }

                        string koOffset = $" + {prefix}_KoOffset";
                        if(vmod.IncludeOffsetInKoHeader)
                        {
                            headers.AppendLine($"#define {definel} {com.Number}{koOffset}");
                            koOffset = "";
                        } else {
                            headers.AppendLine($"#define {definel} {com.Number}");
                        }

                        if(vmod.Name.EndsWith("Share"))
                        {
                            headers.AppendLine($"{line} knx.getGroupObject({definel}{koOffset})");
                        } else if((vmod.IsOpenKnxModule && vmod.Name.EndsWith("Templ")) || !vmod.IsOpenKnxModule)
                        {
                            headers.AppendLine($"{line}Index(X) knx.getGroupObject({prefix}_KoBlockSize * X + {definel}{koOffset})");
                            headers.AppendLine($"{line} knx.getGroupObject({prefix}_KoBlockSize * channelIndex() + {definel}{koOffset})");
                        } else {
                            headers.AppendLine($"{line} knx.getGroupObject({definel})");
                        }
                    }
                    else
                    {
                        headers.AppendLine($"#define APP_Ko{HeaderNameEscape(com.Name)} {com.Number}");
                        headers.AppendLine($"#define KoAPP_{HeaderNameEscape(com.Name)} knx.getGroupObject(APP_Ko{HeaderNameEscape(com.Name)})");
                    }
                }

                XElement xcom = new XElement(Get("ComObject"));
                string id = $"{appVersionMod}_O-";
                if(vbase is Models.Module) id += "2-";
                id += com.Id;
                xcom.SetAttributeValue("Id", id);
                xcom.SetAttributeValue("Name", com.Name);
                xcom.SetAttributeValue("Text", GetDefaultLanguage(com.Text));
                xcom.SetAttributeValue("Number", com.Number);
                xcom.SetAttributeValue("FunctionText", GetDefaultLanguage(com.FunctionText));
                
                if(!com.TranslationText)
                    foreach(Models.Translation trans in com.Text) AddTranslation(trans.Language.CultureCode, id, "Text", trans.Text);
                if(!com.TranslationFunctionText)
                    foreach(Models.Translation trans in com.FunctionText) AddTranslation(trans.Language.CultureCode, id, "FunctionText", trans.Text);
                
                if (com.ObjectSize > 7)
                    xcom.SetAttributeValue("ObjectSize", (com.ObjectSize / 8) + " Byte"+ ((com.ObjectSize > 15) ? "s":""));
                else
                    xcom.SetAttributeValue("ObjectSize", com.ObjectSize + " Bit");

                //TODO implement mayread >=20

                xcom.SetAttributeValue("ReadFlag", com.FlagRead ? "Enabled" : "Disabled");
                xcom.SetAttributeValue("WriteFlag", com.FlagWrite ? "Enabled" : "Disabled");
                xcom.SetAttributeValue("CommunicationFlag", com.FlagComm ? "Enabled" : "Disabled");
                xcom.SetAttributeValue("TransmitFlag", com.FlagTrans ? "Enabled" : "Disabled");
                xcom.SetAttributeValue("UpdateFlag", com.FlagUpdate ? "Enabled" : "Disabled");
                xcom.SetAttributeValue("ReadOnInitFlag", com.FlagOnInit ? "Enabled" : "Disabled");

                if (com.HasDpt && com.Type.Number != "0")
                {
                    if (com.HasDpts)
                        xcom.SetAttributeValue("DatapointType", "DPST-" + com.Type.Number + "-" + com.SubType.Number);
                    else
                        xcom.SetAttributeValue("DatapointType", "DPT-" + com.Type.Number);
                }

                if(baseNumber != null)
                    xcom.SetAttributeValue("BaseNumber", $"{appVersionMod}_A-{baseNumber.Id}");

                xcoms.Add(xcom);
            }

            xparent.Add(xcoms);
        }

        private void ExportComObjectRefs(IVersionBase vbase, XElement xparent)
        {
            Log($"Exportiere ComObjectRefs: {vbase.ComObjectRefs.Count}x");
            if(vbase.ComObjectRefs.Count == 0) return;
            XElement xrefs = new XElement(Get("ComObjectRefs"));

            foreach (ComObjectRef cref in vbase.ComObjectRefs)
            {
                //Log($"    - ComObjectRef {cref.UId} {cref.Name}");
                XElement xcref = new XElement(Get("ComObjectRef"));
                string id = $"{appVersionMod}_O-";
                if(vbase is Models.Module) id += "2-";
                id += cref.ComObjectObject.Id;
                xcref.SetAttributeValue("Id", $"{id}_R-{cref.Id}");
                xcref.SetAttributeValue("RefId", id);
                id += $"_R-{cref.Id}";
                xcref.SetAttributeValue("Id", id);

                if(cref.OverwriteText) {
                    if(!cref.TranslationText)
                        foreach(Models.Translation trans in cref.Text) AddTranslation(trans.Language.CultureCode, id, "Text", trans.Text);
                    xcref.SetAttributeValue("Text", GetDefaultLanguage(cref.Text));
                }
                if(cref.OverwriteFunctionText) {
                    if(!cref.TranslationFunctionText)
                        foreach(Models.Translation trans in cref.FunctionText) AddTranslation(trans.Language.CultureCode, id, "FunctionText", trans.Text);
                    xcref.SetAttributeValue("FunctionText", GetDefaultLanguage(cref.FunctionText));
                }

                if (cref.OverwriteDpt)
                {
                    if (cref.Type.Number == "0")
                    {
                        xcref.SetAttributeValue("DatapointType", "");
                    }
                    else
                    {
                        if (cref.OverwriteDpst)
                            xcref.SetAttributeValue("DatapointType", "DPST-" + cref.Type.Number + "-" + cref.SubType.Number);
                        else
                            xcref.SetAttributeValue("DatapointType", "DPT-" + cref.Type.Number);
                    }
                }

                if(cref.OverwriteOS || (cref.OverwriteDpt && cref.ObjectSize != cref.ComObjectObject.ObjectSize))
                {
                    if (cref.ObjectSize > 7)
                        xcref.SetAttributeValue("ObjectSize", (cref.ObjectSize / 8) + " Byte" + ((cref.ObjectSize > 15) ? "s":""));
                    else
                        xcref.SetAttributeValue("ObjectSize", cref.ObjectSize + " Bit");
                }

                if(vbase.IsComObjectRefAuto && cref.ComObjectObject.UseTextParameter)
                {
                    int nsVersion = int.Parse(currentNamespace.Substring(currentNamespace.LastIndexOf('/')+1));
                    xcref.SetAttributeValue("TextParameterRefId", appVersionMod + (cref.ComObjectObject.ParameterRefObject.ParameterObject.IsInUnion ? "_UP-" : "_P-") + $"{cref.ComObjectObject.ParameterRefObject.ParameterObject.Id}_R-{cref.ComObjectObject.ParameterRefObject.Id}");
                }
                if(!vbase.IsComObjectRefAuto && cref.UseTextParameter)
                {
                    int nsVersion = int.Parse(currentNamespace.Substring(currentNamespace.LastIndexOf('/')+1));
                    xcref.SetAttributeValue("TextParameterRefId", appVersionMod + (cref.ParameterRefObject.ParameterObject.IsInUnion ? "_UP-" : "_P-") + $"{cref.ParameterRefObject.ParameterObject.Id}_R-{cref.ParameterRefObject.Id}");    
                }

                if(cref.OverwriteFC)
                    xcref.SetAttributeValue("CommunicationFlag", cref.FlagComm ? "Enabled" : "Disabled");
                if(cref.OverwriteFOI)
                    xcref.SetAttributeValue("ReadOnInitFlag", cref.FlagOnInit ? "Enabled" : "Disabled");
                if(cref.OverwriteFR)
                    xcref.SetAttributeValue("ReadFlag", cref.FlagRead ? "Enabled" : "Disabled");
                if(cref.OverwriteFT)
                    xcref.SetAttributeValue("TransmitFlag", cref.FlagTrans ? "Enabled" : "Disabled");
                if(cref.OverwriteFU)
                    xcref.SetAttributeValue("UpdateFlag", cref.FlagUpdate ? "Enabled" : "Disabled");
                if(cref.OverwriteFW)
                    xcref.SetAttributeValue("WriteFlag", cref.FlagWrite ? "Enabled" : "Disabled");

                xrefs.Add(xcref);
            }

            xparent.Add(xrefs);
        }

        private void ParseParameter(Parameter para, XElement parent, AppVersion ver, IVersionBase vbase, StringBuilder headers)
        {
            if((headers != null && para.SavePath != SavePaths.Nowhere) || (headers != null && para.IsInUnion && para.UnionObject != null && para.UnionObject.SavePath != SavePaths.Nowhere))
            {
                string lineStart;
                string lineComm = "";
                string prefix = "";
                string extendedPrefix = "";
                string prefixName = "";
                if(vbase is Models.Module mod)
                {
                    if(mod.IsOpenKnxModule)
                    {   
                        OpenKnxModule omod = ver.OpenKnxModules.Single(m => m.Name == mod.Name.Split(' ')[0]);
                        prefix = omod.Prefix;
                        extendedPrefix = prefix  + "_" + mod.Name.Substring(mod.Name.IndexOf(' ')+1);
                    } else {
                        prefix = mod.Prefix;
                        extendedPrefix = prefix;
                    }
                } else {
                    prefix = "APP";
                    extendedPrefix = prefix;
                }
                lineStart = $"#define {extendedPrefix}_{HeaderNameEscape(para.Name)}";
                prefixName = $"{prefix}_{HeaderNameEscape(para.Name)}";
                
                int offset = 0;
                string linePara = $"#define {prefixName}";
                if(para.IsInUnion && para.UnionObject != null)
                {
                    lineComm += $"// UnionOffset: {para.UnionObject.Offset}, ParaOffset: {para.Offset}";
                    offset = para.UnionObject.Offset + para.Offset;
                    linePara += $"\t\t0x{offset.ToString("X4")}";
                } else {
                    lineComm += $"// Offset: {para.Offset}";
                    offset = para.Offset;
                    linePara += $"\t\t0x{para.Offset.ToString("X4")}";
                }
                
                if (para.OffsetBit > 0) lineComm += ", BitOffset: " + para.OffsetBit;
                lineComm += $", Size: {para.ParameterTypeObject.SizeInBit} Bit";
                if (para.ParameterTypeObject.SizeInBit % 8 == 0) lineComm += " (" + (para.ParameterTypeObject.SizeInBit / 8) + " Byte)";
                lineComm += $", Text: {GetDefaultLanguage(para.Text)}";
                headers.AppendLine(linePara);
            
                int totalSize = para.ParameterTypeObject.SizeInBit + para.OffsetBit;
                int totalInBytes = 8;
                if(totalSize > 8 && totalSize <= 16) totalInBytes = 16;
                else if(totalSize <= 32) totalInBytes = 32;
                else totalInBytes = 8; //doesnt care because too big for an int
                int shift = (totalInBytes - para.OffsetBit - (para.ParameterTypeObject.SizeInBit % 8)) % 8;

                ulong mask = 0;
                for(int i = 0; i < para.ParameterTypeObject.SizeInBit; i++)
                    mask += (ulong)Math.Pow(2, i);

                string paraAccess = $"{lineStart.Split(' ')[0]} Param{prefixName}";
                string paraKnxGet = "";

                switch(para.ParameterTypeObject.Type)
                {
                    case ParameterTypes.NumberUInt:
                    case ParameterTypes.NumberInt:
                    case ParameterTypes.Enum:
                    {
                        string ptype = "(uint32_t)";

                        if(para.ParameterTypeObject.Type == ParameterTypes.NumberInt)
                        {
                            if(para.ParameterTypeObject.SizeInBit <= 8)
                                ptype = "(int8_t)";
                            else if(para.ParameterTypeObject.SizeInBit <= 16)
                                ptype = "(int16_t)";
                            else if(para.ParameterTypeObject.SizeInBit <= 32)
                                ptype = "(int32_t)";

                            if(para.ParameterTypeObject.SizeInBit % 8 != 0)
                                throw new Exception("Aktuell sind als Größe von NumberInt nur 8, 16 und 32 möglich");

                            // TODO implement for Int 
                            // #define Abc_LeftShift 2 
                            // #define Abc_Shift 1

                            // result = (int8_t)(knx.paramByte(Abc) << Abc_LeftShift) >> (Abc_LeftShift + Abc_Shift);
                        }

                        if(para.ParameterTypeObject.SizeInBit == 1)
                        {
                            paraKnxGet += $"knx.paramBit(%off%, {para.OffsetBit})";
                        } else {
                            string pshift;
                            if(shift == 0 )
                            {
                                pshift = "";
                            } else {
                                pshift = $" >> {prefixName}_Shift";
                                headers.AppendLine($"#define {prefixName}_Shift\t{shift}");
                            } 
                            string pmask = $" & {prefixName}_Mask";
                            string pAccess = "";

                            if(shift == 0 && para.ParameterTypeObject.SizeInBit % 8 == 0) pmask = "";
                            else
                                headers.AppendLine($"#define {prefixName}_Mask\t0x{mask:X4}");

                            if(totalSize <= 8) pAccess = "paramByte";
                            else if(totalSize <= 16) pAccess = "paramWord";
                            else if(totalSize <= 32) pAccess = "paramInt";
                            else throw new Exception("Size to big for Int/Enum");

                            paraKnxGet += $"({ptype}((knx.{pAccess}(%off%){pshift}){pmask}))";
                        }
                        break;
                    }

                    case ParameterTypes.Float_DPT9:
                    {
                        paraKnxGet += "knx.paramFloat(%off%, Float_Enc_DPT9)";
                        break;
                    }
                    case ParameterTypes.Float_IEEE_Single:
                    {
                        paraKnxGet += "knx.paramFloat(%off%, Float_Enc_IEEE754Single)";
                        break;
                    }
                    case ParameterTypes.Float_IEEE_Double:
                    {
                        paraKnxGet += "knx.paramFloat(%off%, Float_Enc_IEEE754Double)";
                        break;
                    }

                    case ParameterTypes.Color:
                    case ParameterTypes.Text:
                    {
                        paraKnxGet += "knx.paramData(%off%)";
                        break;
                    }

                    case ParameterTypes.Time:
                    {
                        if(para.ParameterTypeObject.Increment == "PackedSecondsAndMilliseconds"
                            || para.ParameterTypeObject.Increment == "PackedDaysHoursMinutesAndSeconds"
                            || para.ParameterTypeObject.Increment == "PackedMinutesSecondsAndMilliseconds")
                        {
                            paraKnxGet += "knx.paramData(%off%)";
                        } else {
                            string pshift;
                            if(shift == 0 )
                            {
                                pshift = "";
                            } else {
                                pshift = $" >> {prefixName}_Shift";
                                headers.AppendLine($"#define {prefixName}_Shift\t{shift}");
                            } 
                            string pmask = $" & {prefixName}_Mask";
                            string pAccess = "";

                            if(shift == 0 && para.ParameterTypeObject.SizeInBit % 8 == 0) pmask = "";
                            else
                                headers.AppendLine($"#define {prefixName}_Mask\t0x{mask:X4}");

                            if(totalSize <= 8) pAccess = "paramByte";
                            else if(totalSize <= 16) pAccess = "paramWord";
                            else if(totalSize <= 32) pAccess = "paramInt";
                            else throw new Exception("Size to big for Int/Enum");

                            paraKnxGet += $"((uint)((knx.{pAccess}(%off%){pshift}){pmask}))";
                        }
                        break;
                    }

                    case ParameterTypes.IpAddress:
                    {
                        paraKnxGet += "knx.paramInt(%off%)";
                        break;
                    }

                    default:
                        throw new NotImplementedException($"Export Parameter ParameterTyp '{para.ParameterTypeObject.Type.ToString()} wird nicht unterstützt.");
                }
                
                string offsetOut = offset.ToString();
                if(vbase is Models.Module mod2)
                {
                    List<DynModule> mods = new List<DynModule>();
                    Helper.GetModules(ver.Dynamics[0], mods);
                    int modCount = mods.Count(m => m.ModuleUId == mod2.UId);
                    
                    if(modCount > 1)
                    {
                        string off = paraKnxGet.Replace("%off%", $"({extendedPrefix}_ParamBlockOffset + {extendedPrefix}_ParamBlockSize * X + {prefixName})");
                        headers.AppendLine(lineComm);
                        headers.AppendLine($"{paraAccess}Index(X) {off}");
                        off = paraKnxGet.Replace("%off%", $"({extendedPrefix}_ParamBlockOffset + {extendedPrefix}_ParamBlockSize * channelIndex() + {prefixName})");
                        headers.AppendLine(lineComm);
                        headers.AppendLine($"{paraAccess} {off}");
                    } else {
                        string off = paraKnxGet.Replace("%off%", $"({extendedPrefix}_ParamBlockOffset + {prefixName})");
                        headers.AppendLine(lineComm);
                        headers.AppendLine($"{paraAccess} {off}");
                    }
                } else {
                    paraKnxGet = paraKnxGet.Replace("%off%", prefixName);
                    headers.AppendLine(lineComm);
                    headers.AppendLine($"{paraAccess} {paraKnxGet}");
                }

                if(para.ParameterTypeObject.Name.StartsWith("DelayTime"))
                {
                    headers.AppendLine($"{paraAccess}MS (paramDelay(Param{prefixName}))");
                }
            }

            XElement xpara = new XElement(Get("Parameter"));
            string id = appVersionMod + (para.IsInUnion ? "_UP-" : "_P-") + para.Id;
            xpara.SetAttributeValue("Id", id);
            xpara.SetAttributeValue("Name", para.Name);
            xpara.SetAttributeValue("ParameterType", $"{appVersion}_PT-{GetEncoded(para.ParameterTypeObject.Name)}");

            if(!para.TranslationText)
                foreach(Models.Translation trans in para.Text) AddTranslation(trans.Language.CultureCode, id, "Text", trans.Text);

            if(!para.TranslationSuffix)
                foreach(Models.Translation trans in para.Suffix) AddTranslation(trans.Language.CultureCode, id, "SuffixText", trans.Text);

            if(!para.IsInUnion) {
                switch(para.SavePath) {
                    case SavePaths.Memory:
                    {
                        XElement xparamem = new XElement(Get("Memory"));
                        Memory mem = para.SaveObject as Memory;
                        if(mem == null) throw new Exception("Parameter soll in Memory gespeichert werden, aber der Typ von SaveObject ist kein Memory: " + para.SaveObject.GetType().ToString());
                        string memid = appVersion;
                        if (mem.Type == MemoryTypes.Absolute)
                            memid += $"_AS-{mem.Address:X4}";
                        else
                            memid += $"_RS-04-{mem.Offset:X5}";
                        xparamem.SetAttributeValue("CodeSegment", memid);
                        xparamem.SetAttributeValue("Offset", para.Offset);
                        xparamem.SetAttributeValue("BitOffset", para.OffsetBit);

                        if(vbase is Models.Module mod)
                        {
                            xparamem.SetAttributeValue("BaseOffset", $"{appVersionMod}_A-{mod.ParameterBaseOffset.Id}");
                        }

                        xpara.Add(xparamem);
                        break;
                    }

                    case SavePaths.Property:
                    {
                        XElement xparamem = new XElement(Get("Property"));
                        Property prop = para.SaveObject as Property;
                        if(prop == null) throw new Exception("Parameter soll in Property gespeichert werden, aber der Typ von SaveObject ist kein Property: " + para.SaveObject.GetType().ToString());
                        
                        xparamem.SetAttributeValue("ObjectIndex", prop.ObjectType);
                        xparamem.SetAttributeValue("PropertyId", prop.PropertyId);
                        xparamem.SetAttributeValue("Offset", prop.Offset);
                        xparamem.SetAttributeValue("BitOffset", prop.OffsetBit);
                        break;
                    }
                }
            }
            else
            {
                xpara.SetAttributeValue("Offset", para.Offset);
                xpara.SetAttributeValue("BitOffset", para.OffsetBit);
                if (para.IsUnionDefault)
                    xpara.SetAttributeValue("DefaultUnionParameter", "true");
            }
            
            xpara.SetAttributeValue("Text", GetDefaultLanguage(para.Text));
            if (para.Access != ParamAccess.ReadWrite) xpara.SetAttributeValue("Access", para.Access);
            if (!string.IsNullOrWhiteSpace(GetDefaultLanguage(para.Suffix))) xpara.SetAttributeValue("SuffixText", GetDefaultLanguage(para.Suffix));
            
            if(para.ParameterTypeObject.Type == ParameterTypes.Picture)
                xpara.SetAttributeValue("Value", "");
            else if(para.ParameterTypeObject.Type == ParameterTypes.RawData)
            {
                xpara.SetAttributeValue("Value", ConvertHexStringToBas64(para.Value));
            }
            else if(para.ParameterTypeObject.Type == ParameterTypes.Float_DPT9 ||
                para.ParameterTypeObject.Type == ParameterTypes.Float_IEEE_Single ||
                para.ParameterTypeObject.Type == ParameterTypes.Float_IEEE_Double) {
                float temp_float = float.Parse(para.Value);
                string value = temp_float.ToString("E").Replace(",", ".");
                int to_short = 22 - value.Length;
                string add = "";
                for(int i = 0; i < to_short; i++)
                    add += "0";
                value = value.Replace("E", add + "E");
                xpara.SetAttributeValue("Value", value);
            } else {
                xpara.SetAttributeValue("Value", para.Value);
            }

            parent.Add(xpara);
        }

        private string ConvertHexStringToBas64(string hexString)
        {
            byte[] data = new byte[hexString.Length / 2];
            for (int index = 0; index < data.Length; index++)
            {
                string byteValue = hexString.Substring(index * 2, 2);
                data[index] = byte.Parse(byteValue, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
            }

            return Convert.ToBase64String(data); 
        }

        #region Create Dyn Stuff

        private void HandleSubItems(IDynItems parent, XElement xparent, AppVersion ver = null, IVersionBase vbase = null)
        {
            foreach (IDynItems item in parent.Items)
            {
                XElement xitem = null;

                switch (item)
                {
                    case DynChannel dc:
                    case DynChannelIndependent dci:
                        xitem = Handle(item, xparent);
                        break;

                    case DynParaBlock dpb:
                        xitem = HandleBlock(dpb, xparent);
                        break;

                    case DynParameter dp:
                        HandleParam(dp, xparent, ver);
                        break;

                    case IDynChoose dch:
                        xitem = HandleChoose(dch, xparent);
                        break;

                    case IDynWhen dw:
                        xitem = HandleWhen(dw, xparent);
                        break;

                    case DynComObject dc:
                        HandleCom(dc, xparent);
                        break;

                    case DynSeparator ds:
                        HandleSep(ds, xparent);
                        break;

                    case DynModule dm:
                        HandleMod(dm, xparent, ver, vbase);
                        break;

                    case DynAssign da:
                        HandleAssign(da, xparent);
                        break;

                    case DynRepeat dr:
                        xitem = HandleRepeat(dr, xparent);
                        break;

                    case DynButton db:
                        HandleButton(db, xparent);
                        break;

                    default:
                        throw new Exception("Nicht behandeltes dynamisches Element: " + item.ToString());
                }

                if (item.Items != null && xitem != null)
                    HandleSubItems(item, xitem, ver, vbase);
            }
        }

        private int channelCounter = 1;
        private XElement Handle(IDynItems ch, XElement parent)
        {
            XElement channel = new XElement(Get("ChannelIndependentBlock"));
            parent.Add(channel);

            if (ch is DynChannel dch)
            {
                channel.Name = Get("Channel");
                if (dch.UseTextParameter)
                    channel.SetAttributeValue("TextParameterRefId", appVersionMod + (dch.ParameterRefObject.ParameterObject.IsInUnion ? "_UP-" : "_P-") + $"{dch.ParameterRefObject.ParameterObject.Id}_R-{dch.ParameterRefObject.Id}");

                dch.Number = channelCounter.ToString();
                channelCounter++;
                
                channel.SetAttributeValue("Text", GetDefaultLanguage(dch.Text));
                if (!dch.TranslationText)
                    foreach (Models.Translation trans in dch.Text) AddTranslation(trans.Language.CultureCode, $"{appVersionMod}_CH-{dch.Number}", "Text", trans.Text);
                
                channel.SetAttributeValue("Number", dch.Number);
                channel.SetAttributeValue("Id", $"{appVersionMod}_CH-{dch.Number}");
                channel.SetAttributeValue("Name", ch.Name);

                if(dch.UseIcon)
                {
                    channel.SetAttributeValue("Icon", dch.IconObject.Name);
                    if(!iconsApp.Contains(dch.IconObject))
                        iconsApp.Add(dch.IconObject);
                }

                if(dch.Access != ParamAccess.ReadWrite)
                    channel.SetAttributeValue("Access", dch.Access.ToString());
            }

            return channel;
        }

        private void HandleCom(DynComObject com, XElement parent)
        {
            XElement xcom = new XElement(Get("ComObjectRefRef"));
            string id = $"{appVersionMod}_O-";

            if(appVersion != appVersionMod) id += "2-";
            id += $"{com.ComObjectRefObject.ComObjectObject.Id}_R-{com.ComObjectRefObject.Id}";

            xcom.SetAttributeValue("RefId", id);
            parent.Add(xcom);
        }

        private void HandleMod(DynModule mod, XElement parent, AppVersion ver, IVersionBase vbase)
        {
            XElement xmod = new XElement(Get("Module"));
            if(mod.Id == -1)
            {
                string name = "base";
                if(vbase is Models.Module vmod)
                {
                    name = vmod.Name;
                }
                string key = name + mod.ModuleObject.Name;
                if(!moduleCounter.ContainsKey(key))
                    moduleCounter.Add(key, 1);
                else if(moduleCounter[key] == -1)
                    moduleCounter[key] = 1;
                else
                    moduleCounter[key]++;
                mod.Id = moduleCounter[key];
            }
            xmod.SetAttributeValue("Id", $"{appVersionMod}_{(appVersionMod.Contains("_MD-") ? "SM":"MD")}-{mod.ModuleObject.Id}_M-{mod.Id}");
            xmod.SetAttributeValue("RefId", mod.ModuleObject.ExportHelper); // $"{appVersionMod}_MD-{mod.ModuleObject.Id}");

            int argCounter = 1;
            foreach(DynModuleArg arg in mod.Arguments)
            {
                XElement xarg = new XElement(Get(arg.Argument.Type.ToString() + "Arg"));
                xarg.SetAttributeValue("RefId", arg.Argument.ExportHelper); // $"{appVersion}_MD-{mod.ModuleObject.Id}_A-{arg.Argument.Id}");

                //M-0002_A-20DE-22-4365-O000A_MD-3_M-18_A-3
                if(arg.Argument.Type == ArgumentTypes.Text)
                    xarg.SetAttributeValue("Id", $"{appVersion}_MD-{mod.ModuleObject.Id}_M-{mod.Id}_A-{argCounter}");

                if(arg.UseAllocator)
                {
                    xarg.SetAttributeValue("AllocatorRefId", $"{appVersion}_L-{arg.Allocator.Id}");
                } else {
                    xarg.SetAttributeValue("Value", arg.Value);
                }
                xmod.Add(xarg);
                argCounter++;
            }

            parent.Add(xmod);
        }

        private int separatorCounter = 1;
        private void HandleSep(DynSeparator sep, XElement parent)
        {
            XElement xsep = new XElement(Get("ParameterSeparator"));
            sep.Id = separatorCounter++;
            xsep.SetAttributeValue("Id", $"{appVersionMod}_PS-{sep.Id}");
            xsep.SetAttributeValue("Text", GetDefaultLanguage(sep.Text));
            if(sep.Hint != SeparatorHint.None)
            {
                xsep.SetAttributeValue("UIHint", sep.Hint.ToString());
            }
            if(!string.IsNullOrEmpty(sep.Cell))
                xsep.SetAttributeValue("Cell", sep.Cell);
            
            if(sep.UseIcon)
            {
                xsep.SetAttributeValue("Icon", sep.IconObject.Name);
                if(!iconsApp.Contains(sep.IconObject))
                    iconsApp.Add(sep.IconObject);
            }

            if(sep.Access != ParamAccess.ReadWrite)
                xsep.SetAttributeValue("Access", sep.Access.ToString());

            parent.Add(xsep);

            if(!sep.TranslationText)
                foreach(Models.Translation trans in sep.Text) AddTranslation(trans.Language.CultureCode, $"{appVersionMod}_PS-{sep.Id}", "Text", trans.Text);
        }

        private XElement HandleChoose(IDynChoose cho, XElement parent)
        {
            XElement xcho = new XElement(Get("choose"));
            parent.Add(xcho);
            if(!cho.IsGlobal)
                xcho.SetAttributeValue("ParamRefId", appVersionMod + (cho.ParameterRefObject.ParameterObject.IsInUnion ? "_UP-" : "_P-") + $"{cho.ParameterRefObject.ParameterObject.Id}_R-{cho.ParameterRefObject.Id}");
            else
                xcho.SetAttributeValue("ParamRefId", appVersion + (cho.ParameterRefObject.ParameterObject.IsInUnion ? "_UP-" : "_P-") + $"{cho.ParameterRefObject.ParameterObject.Id}_R-{cho.ParameterRefObject.Id}");
            return xcho;
        }

        private XElement HandleWhen(IDynWhen when, XElement parent)
        {
            XElement xwhen = new XElement(Get("when"));
            parent.Add(xwhen);

            if (when.IsDefault)
                xwhen.SetAttributeValue("default", "true");
            else
                xwhen.SetAttributeValue("test", when.Condition);

            return xwhen;
        }

        int pbCounter = 1;
        private XElement HandleBlock(DynParaBlock bl, XElement parent)
        {
            XElement block = new XElement(Get("ParameterBlock"));
            parent.Add(block);

            bl.Id = pbCounter++;

            //Wenn Block InLine ist, kann kein ParamRef angegeben werden
            if(bl.IsInline)
            {
                block.SetAttributeValue("Id", $"{appVersionMod}_PB-{bl.Id}");
                block.SetAttributeValue("Inline", "true");
            } else {
                if(bl.UseParameterRef)
                {
                    block.SetAttributeValue("Id", $"{appVersionMod}_PB-{bl.ParameterRefObject.Id}");
                    block.SetAttributeValue("ParamRefId", appVersionMod + (bl.ParameterRefObject.ParameterObject.IsInUnion ? "_UP-" : "_P-") + $"{bl.ParameterRefObject.ParameterObject.Id}_R-{bl.ParameterRefObject.Id}");
                }
                else
                {
                    block.SetAttributeValue("Id", $"{appVersionMod}_PB-{bl.Id}");
                    string dText = GetDefaultLanguage(bl.Text);
                    //Wenn Block InLine ist, kann kein Text angegeben werden
                    if (!string.IsNullOrEmpty(dText))
                    {
                        block.SetAttributeValue("Text", dText);
                        if (!bl.TranslationText)
                            foreach (Models.Translation trans in bl.Text) AddTranslation(trans.Language.CultureCode, $"{appVersionMod}_PB-{bl.Id}", "Text", trans.Text);
                    }
                }
            }

            if(bl.Layout != BlockLayout.List)
            {
                block.SetAttributeValue("Layout", bl.Layout.ToString());

                if(bl.Rows.Count > 0)
                {
                    int rowCounter = 1;
                    XElement xrows = new XElement(Get("Rows"));
                    foreach(ParameterBlockRow row in bl.Rows)
                    {
                        XElement xrow = new XElement(Get("Row"));
                        xrow.SetAttributeValue("Id", $"{appVersionMod}_PB-{bl.Id}_R-{rowCounter++}");
                        xrow.SetAttributeValue("Name", row.Name);
                        xrows.Add(xrow);
                    }
                    block.Add(xrows);
                }

                if(bl.Columns.Count > 0)
                {
                    int colCounter = 1;
                    XElement xcols = new XElement(Get("Columns"));
                    foreach(ParameterBlockColumn col in bl.Columns)
                    {
                        XElement xcol = new XElement(Get("Column"));
                        xcol.SetAttributeValue("Id", $"{appVersionMod}_PB-{bl.Id}_C-{colCounter++}");
                        xcol.SetAttributeValue("Name", col.Name);
                        xcol.SetAttributeValue("Width", $"{col.Width}%");
                        xcols.Add(xcol);
                    }
                    block.Add(xcols);
                }
            }

            if(!string.IsNullOrEmpty(bl.Name))
                block.SetAttributeValue("Name", bl.Name);

            //Wenn Block InLine ist, kann kein TextParameter angegeben werden
            if (bl.UseTextParameter && !bl.IsInline)
                block.SetAttributeValue("TextParameterRefId", appVersionMod + (bl.TextRefObject.ParameterObject.IsInUnion ? "_UP-" : "_P-") + $"{bl.TextRefObject.ParameterObject.Id}_R-{bl.TextRefObject.Id}");

            if(bl.ShowInComObjectTree)
                block.SetAttributeValue("ShowInComObjectTree", "true");

            if(bl.UseIcon)
            {
                block.SetAttributeValue("Icon", bl.IconObject.Name);
                if(!iconsApp.Contains(bl.IconObject))
                    iconsApp.Add(bl.IconObject);
            }

            if(bl.Access != ParamAccess.ReadWrite)
                block.SetAttributeValue("Access", bl.Access.ToString());

            return block;
        }

        private void HandleParam(DynParameter pa, XElement parent, AppVersion vbase)
        {
            XElement xpara = new XElement(Get("ParameterRefRef"));
            parent.Add(xpara);
            xpara.SetAttributeValue("RefId", appVersionMod + (pa.ParameterRefObject.ParameterObject.IsInUnion ? "_UP-" : "_P-") + $"{pa.ParameterRefObject.ParameterObject.Id}_R-{pa.ParameterRefObject.Id}");
            if(!string.IsNullOrEmpty(pa.Cell))
                xpara.SetAttributeValue("Cell", pa.Cell);

            if(vbase.IsHelpActive && pa.HasHelptext)
            {
                xpara.SetAttributeValue("HelpContext", pa.Helptext.Name);
            }

            if(pa.UseIcon)
            {
                xpara.SetAttributeValue("Icon", pa.IconObject.Name);
                if(!iconsApp.Contains(pa.IconObject))
                    iconsApp.Add(pa.IconObject);
            }
        }
        
        private XElement HandleAssign(DynAssign da, XElement parent)
        {
            XElement xcho = new XElement(Get("Assign"));
            parent.Add(xcho);
            xcho.SetAttributeValue("TargetParamRefRef", appVersionMod + (da.TargetObject.ParameterObject.IsInUnion ? "_UP-" : "_P-") + $"{da.TargetObject.ParameterObject.Id}_R-{da.TargetObject.Id}");
            if(string.IsNullOrEmpty(da.Value))
                xcho.SetAttributeValue("SourceParamRefRef", appVersionMod + (da.SourceObject.ParameterObject.IsInUnion ? "_UP-" : "_P-") + $"{da.SourceObject.ParameterObject.Id}_R-{da.SourceObject.Id}");
            else
                xcho.SetAttributeValue("Value", da.Value);
            return xcho;
        }

        int repCount = 1;
        private XElement HandleRepeat(DynRepeat dr, XElement parent)
        {
            XElement xcho = new XElement(Get("Repeat"));
            parent.Add(xcho);
            dr.Id = repCount++;
            xcho.SetAttributeValue("Id", $"{appVersionMod}_X-{dr.Id}");
            xcho.SetAttributeValue("Name", dr.Name);
            xcho.SetAttributeValue("Count", dr.Count);
            if(dr.UseParameterRef)
                xcho.SetAttributeValue("ParameterRefId", appVersionMod + (dr.ParameterRefObject.ParameterObject.IsInUnion ? "_UP-" : "_P-") + $"{dr.ParameterRefObject.ParameterObject.Id}_R-{dr.ParameterRefObject.Id}");
            return xcho;
        }

        int btnCounter = 1;
        private void HandleButton(DynButton db, XElement parent)
        {
            XElement xbtn = new XElement(Get("Button"));
            string id = $"{appVersionMod}_B-{btnCounter++}";
            xbtn.SetAttributeValue("Id", id);
            xbtn.SetAttributeValue("Text", GetDefaultLanguage(db.Text));

            int ns = int.Parse(currentNamespace.Substring(currentNamespace.LastIndexOf('/') + 1));
            if(ns > 14)
                xbtn.SetAttributeValue("Name", db.Name);

            xbtn.SetAttributeValue("EventHandler", $"button{HeaderNameEscape(db.Name)}");

            if(!string.IsNullOrEmpty(db.Cell))
                xbtn.SetAttributeValue("Cell", db.Cell);
            if(!string.IsNullOrEmpty(db.EventHandlerParameters))
                xbtn.SetAttributeValue("EventHandlerParameters", db.EventHandlerParameters);
            if(!string.IsNullOrEmpty(db.Online))
                xbtn.SetAttributeValue("EventHandlerOnline", db.Online);

            if(db.UseIcon)
            {
                xbtn.SetAttributeValue("Icon", db.IconObject.Name);
                if(!iconsApp.Contains(db.IconObject))
                    iconsApp.Add(db.IconObject);
            }
            if (db.UseTextParameter)
                xbtn.SetAttributeValue("TextParameterRefId", appVersionMod + (db.TextRefObject.ParameterObject.IsInUnion ? "_UP-" : "_P-") + $"{db.TextRefObject.ParameterObject.Id}_R-{db.TextRefObject.Id}");

            parent.Add(xbtn);

            if(!db.TranslationText)
            {
                foreach(Translation trans in db.Text) AddTranslation(trans.Language.CultureCode, id, "Text", trans.Text);
            }

            string function = $"function button{HeaderNameEscape(db.Name)}(device, online, progress, context)";
            function += "\r\n{\r\n";
            function += db.Script;
            function += "\r\n}\r\n";
            buttonScripts.Add(function);
        }
        #endregion

        private bool CheckSections(CatalogItem parent)
        {
            bool flag = false;

            foreach (CatalogItem item in parent.Items)
            {
                if (item.IsSection)
                {
                    if (CheckSections(item)) flag = true;
                }
                else
                {
                    flag = true;
                }
            }
            return flag;
        }

        private void GetCatalogItems(CatalogItem item, XElement parent, Dictionary<string, string> productIds, Dictionary<string, string> hardwareIds)
        {
            if (item.IsSection)
            {
                XElement xitem = new XElement(Get("CatalogSection"));
                string id;
                
                if (CheckSections(item))
                {
                    if (item.Parent.Parent == null)
                    {
                        id = $"M-{GetManuId()}_CS-" + GetEncoded(item.Number);
                        xitem.SetAttributeValue("Id", id);
                    }
                    else
                    {
                        id = parent.Attribute("Id").Value;
                        id += "-" + GetEncoded(item.Number);
                        xitem.SetAttributeValue("Id", id);
                    }

                    xitem.SetAttributeValue("Name", GetDefaultLanguage(item.Text));
                    xitem.SetAttributeValue("Number", item.Number);
                    xitem.SetAttributeValue("DefaultLanguage", currentLang);
                    parent.Add(xitem);

                    foreach(Translation trans in item.Text) AddTranslation(trans.Language.CultureCode, id, "Name", trans.Text);
                }

                foreach (CatalogItem sub in item.Items)
                    GetCatalogItems(sub, xitem, productIds, hardwareIds);
            }
            else
            {
                XElement xitem = new XElement(Get("CatalogItem"));

                string id = $"M-{GetManuId()}";
                id += $"_H-{GetEncoded(general.Info.SerialNumber)}-{general.Info.Version}";
                id += $"_HP-{GetAppId(general.Info.AppNumber)}-{general.Application.Number.ToString("X2")}-0000";
                string parentId = parent.Attribute("Id").Value;
                parentId = parentId.Substring(parentId.LastIndexOf("_CS-") + 4);
                id += $"_CI-{GetEncoded(general.Info.OrderNumber)}-{GetEncoded(item.Number)}";

                xitem.SetAttributeValue("Id", id);
                xitem.SetAttributeValue("Name", GetDefaultLanguage(general.Info.Text));
                xitem.SetAttributeValue("Number", item.Number);
                xitem.SetAttributeValue("VisibleDescription", GetDefaultLanguage(general.Info.Description));
                xitem.SetAttributeValue("ProductRefId", productIds[general.Info.Name]);
                string hardid = general.Info.Version + "-" + GetAppId(general.Info.AppNumber) + "-" + general.Application.Number;
                xitem.SetAttributeValue("Hardware2ProgramRefId", hardwareIds[hardid]);
                xitem.SetAttributeValue("DefaultLanguage", currentLang);
                parent.Add(xitem);

                foreach(Translation trans in general.Info.Text) AddTranslation(trans.Language.CultureCode, id, "Name", trans.Text);
                foreach(Translation trans in general.Info.Description) AddTranslation(trans.Language.CultureCode, id, "VisibleDescription", trans.Text);
            }
        }

        public async Task SignOutput(string path, string filePath, int namespaceversion)
        {
            string manu = Directory.GetDirectories(path).First();
            manu = manu.Substring(manu.LastIndexOf('\\') + 1);

            string etsPath = SignHelper.FindEtsPath(namespaceversion);
            //Log($"Verwende ETS: {etsPath}");

            Task sign = Task.Run(() => {
                SignHelper.SignFiles(path, manu, namespaceversion);
            });
            await sign.WaitAsync(new CancellationTokenSource().Token);
            
            if(File.Exists(filePath))
                File.Delete(filePath);
            SignHelper.ZipFolder(path, filePath);
        }

        public static string GetEncoded(string input)
        {
            if(input == null)
            {
                Debug.WriteLine("GetEncoded: Input was null");
                return "";
            }
            input = input.Replace(".", ".2E");

            input = input.Replace("%", ".25");
            input = input.Replace(" ", ".20");
            input = input.Replace("!", ".21");
            input = input.Replace("\"", ".22");
            input = input.Replace("#", ".23");
            input = input.Replace("$", ".24");
            input = input.Replace("&", ".26");
            input = input.Replace("(", ".28");
            input = input.Replace(")", ".29");
            input = input.Replace("+", ".2B");
            input = input.Replace(",", ".2C");
            input = input.Replace("-", ".2D");
            input = input.Replace("/", ".2F");
            input = input.Replace(":", ".3A");
            input = input.Replace(";", ".3B");
            input = input.Replace("<", ".3C");
            input = input.Replace("=", ".3D");
            input = input.Replace(">", ".3E");
            input = input.Replace("?", ".3F");
            input = input.Replace("@", ".40");
            input = input.Replace("[", ".5B");
            input = input.Replace("\\", ".5C");
            input = input.Replace("]", ".5D");
            input = input.Replace("^", ".5C");
            input = input.Replace("_", ".5F");
            input = input.Replace("{", ".7B");
            input = input.Replace("|", ".7C");
            input = input.Replace("}", ".7D");
            input = input.Replace("°", ".C2.B0");
            return input;
        }

        public string GetDefaultLanguage(ObservableCollection<Translation> trans)
        {
            return trans.Single(e => e.Language.CultureCode == currentLang).Text;
        }

        public string GetRelPath(params string[] path)
        {
            List<string> paths = new List<string>() { AppDomain.CurrentDomain.BaseDirectory, "Output" };
            paths.AddRange(path);
            return System.IO.Path.Combine(paths.ToArray());
        }

        public string GetRelPath()
        {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
        }

        public XName Get(string name)
        {
            return XName.Get(name, currentNamespace);
        }

        public void SetNamespace(int ns)
        {
            currentNamespace = $"http://knx.org/xml/project/{ns}";
        }

        private void Log(string message)
        {
            Debug.WriteLine($"       {message}");
            if(actions != null)
                actions.Add(new() { Text = $"       {message}"});
        }

        private void LogE(string message)
        {
            Debug.WriteLine($"       {message}");
            if(actions != null)
                actions.Add(new() { Text = $"       {message}", State = PublishState.Fail});
        }

        public XElement CreateNewXML(string manu)
        {
            XElement xmanu = new XElement(Get("Manufacturer"));
            xmanu.SetAttributeValue("RefId", manu);

            XElement knx = new XElement(Get("KNX"));
            //this makes icons work...
            knx.SetAttributeValue("CreatedBy", "kaenx-creator");
            knx.SetAttributeValue("ToolVersion", "1.0.0");

            doc = new XDocument(knx);
            doc.Root.Add(new XElement(Get("ManufacturerData"), xmanu));
            return xmanu;
        }

        private string GetManuId()
        {
            return general.IsOpenKnx ? "00FA" : general.ManufacturerId.ToString("X4");
        }

        private string GetAppId(int number)
        {
            return general.IsOpenKnx ? general.ManufacturerId.ToString("X2") + number.ToString("X2") : number.ToString("X4");
        }
    }
}