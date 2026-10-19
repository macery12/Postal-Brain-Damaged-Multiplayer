using Hyperstrange.PBD.UI.Components;
using PBDMP.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PBDMP.Plugin;

/// <summary>Native themed save browser; listing and checksum reads run away from Unity's main thread.</summary>
internal sealed class NativeCoopSaves
{
    internal GameObject Root {get;}
    private readonly MainHyperButton[] _rows;
    private readonly MainHyperButton _previous,_next,_new,_save,_load,_quick,_checkpoint,_back;
    private readonly TMP_InputField _name;
    private readonly TMP_Text _status,_pageText;
    private readonly RectTransform _spiral;
    private readonly Action<string,string,bool> _saveAction;
    private readonly Action<string> _loadAction;
    private Task<SaveListing[]>? _listing;
    private SaveListing[] _slots=Array.Empty<SaveListing>();
    private int _page,_selected=-1;
    private float _refreshAt;
    internal NativeCoopSaves(NativeUiKit kit,Transform parent,Action<string,string,bool> save,Action<string> load,Action checkpoint,Action back)
    {
        _saveAction=save;_loadAction=load;
        Root=NativeUiKit.Rect("Co-op Saves dialog",parent,Vector2.zero,Vector2.zero);NativeUiKit.Stretch(Root.GetComponent<RectTransform>());
        kit.DimAt(Root.transform,"Save dim",.8f);
        var box=kit.BoxAt(Root.transform,"Co-op Saves panel",new Vector2(1200,850),out _spiral).transform;
        kit.HeadingAt(box,"Title","CO-OP SAVES",new Vector2(600,782),new Vector2(1000,60),40);
        kit.TextAt(box,"Help","P1 SAVES THE WORLD. PERSONAL PICKUPS STAY PERSONAL.",new Vector2(600,728),new Vector2(1050,40),21);
        _rows=Enumerable.Range(0,6).Select(i=>
        {
            var button=kit.ActionAt(box,"Empty",new Vector2(600,665-i*72),new Vector2(1060,64),()=>Select(_page*6+i));
            button.LabelText.fontSize=23;button.LabelText.enableWordWrapping=false;button.LabelText.overflowMode=TextOverflowModes.Ellipsis;
            return button;
        }).ToArray();
        _new=kit.ActionAt(box,"New slot",new Vector2(180,230),new Vector2(230,54),()=>{_selected=-1;_name!.text="Co-op save";RefreshButtons();});
        _previous=kit.ActionAt(box,"Previous",new Vector2(700,230),new Vector2(210,54),()=>{_page=Math.Max(0,_page-1);Render();});
        _next=kit.ActionAt(box,"Next",new Vector2(1010,230),new Vector2(210,54),()=>{_page++;Render();});
        _pageText=kit.TextAt(box,"Page","",new Vector2(855,230),new Vector2(90,40),22);
        _name=kit.InputAt(box,"Save name","Co-op save",new Vector2(435,158),new Vector2(720,60));_name.characterLimit=80;_name.textComponent.richText=false;
        _save=kit.ActionAt(box,"Save new slot",new Vector2(1000,158),new Vector2(240,60),()=>
        {
            var selected=Selected;var overwrite=selected?.Slot.StartsWith("manual-",StringComparison.Ordinal)==true;
            _saveAction(overwrite?selected!.Slot:"manual-"+Guid.NewGuid().ToString("N"),_name.text,overwrite);_refreshAt=0;
        });
        _quick=kit.ActionAt(box,"Quicksave",new Vector2(190,83),new Vector2(240,60),()=>{_saveAction("quick","Quicksave",false);_refreshAt=0;});
        _load=kit.ActionAt(box,"Load selected",new Vector2(470,83),new Vector2(240,60),()=>{if(Selected is {} selected) _loadAction(selected.Slot);});
        _checkpoint=kit.ActionAt(box,"Restore checkpoint",new Vector2(750,83),new Vector2(280,60),checkpoint);
        _checkpoint.LabelText.fontSize=18;
        _load.LabelText.fontSize=21;_quick.LabelText.fontSize=24;_save.LabelText.fontSize=22;
        _back=kit.ActionAt(box,"Back",new Vector2(1020,83),new Vector2(200,60),back);
        _status=kit.TextAt(box,"Save status","Host a lobby to save or resume a co-op game.",new Vector2(600,32),new Vector2(1080,38),20);
        Root.SetActive(false);
    }
    internal bool Open=>Root.activeSelf;
    internal void Show(bool open) {Root.SetActive(open);if(open) {_refreshAt=0;_selected=-1;_page=0;Render();}}
    internal Selectable[] Controls=>_rows.Where(r=>r.gameObject.activeSelf && r.interactable).Cast<Selectable>()
        .Concat(new Selectable[]{_new,_previous,_next,_name,_save,_quick,_load,_checkpoint,_back}.Where(s=>s.interactable)).ToArray();
    private SaveListing? Selected=>_selected>=0 && _selected<_slots.Length?_slots[_selected]:null;
    private void Select(int index) {_selected=index;if(Selected is {} selected) _name.text=selected.Name;Render();}
    internal void Tick()
    {
        if(!Open) return;_spiral.localRotation=Quaternion.Euler(0,0,-Time.unscaledTime*4);
        if(_listing?.IsCompleted==true)
        {
            try {var selected=Selected?.Slot;_slots=_listing.GetAwaiter().GetResult();_selected=Array.FindIndex(_slots,s=>s.Slot==selected);Render();}
            catch(Exception e) {_status.text="Could not list saves: "+e.Message;}
            _listing=null;
        }
        if(_listing==null && Time.unscaledTime>=_refreshAt)
        {_refreshAt=Time.unscaledTime+3;_listing=Task.Run(()=>new CoopSaveStore(CoopSaveService.Root).List());}
        RefreshButtons();
        _status.text=SessionLifecycle.Current?.IsClient==true?"P1 controls co-op saves and loading.":SessionLifecycle.Current?.InSurvival==true?CoopSaveService.SurvivalUnsaved:CoopSaveService.Current is {} service?service.Status.Length>0?service.Status:"Co-op saves are separate from solo saves.":"Host a lobby to save or resume a co-op game.";
    }
    private void Render()
    {
        _page=Math.Clamp(_page,0,Math.Max(0,(_slots.Length-1)/6));
        for(var i=0;i<_rows.Length;i++)
        {
            var index=_page*6+i;_rows[i].gameObject.SetActive(index<_slots.Length);
            if(index>=_slots.Length) continue;
            var slot=_slots[index];var map=SessionLifecycle.FindLevel(slot.Level)?.name.Replace("LEVEL_AD_","",StringComparison.Ordinal).Replace('_',' ')??"";var label=(index==_selected?"> ":"")+slot.Name+"  /  "+map+"  /  "+slot.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm")+(slot.Error!=null?"  /  UNREADABLE":slot.Backup?"  /  RECOVERY BACKUP":"");
            _rows[i].LabelText.richText=false;NativeUiKit.SetLabel(_rows[i],label);
        }
        _pageText.text=(_page+1)+" / "+Math.Max(1,(_slots.Length+5)/6);
        NativeUiKit.SetButtonState(_previous,_page>0);NativeUiKit.SetButtonState(_next,(_page+1)*6<_slots.Length);RefreshButtons();
    }
    private void RefreshButtons()
    {
        var service=CoopSaveService.Current;var host=SessionLifecycle.Current is {IsClient:false};var busy=service?.Busy==true;
        NativeUiKit.SetButtonState(_save,host && !busy && SessionLifecycle.Current!.NativeReady && !SessionLifecycle.Current.InSurvival && SharedWorld.Current?.Active==true);
        NativeUiKit.SetButtonState(_quick,_save.interactable);
        NativeUiKit.SetButtonState(_load,host && !busy && Selected is {Error:null});
        NativeUiKit.SetButtonState(_checkpoint,host && !busy && _slots.Any(s=>s.Slot.StartsWith("auto-",StringComparison.Ordinal) && s.Error==null));
        NativeUiKit.SetLabel(_save,Selected?.Slot.StartsWith("manual-",StringComparison.Ordinal)==true?"Overwrite slot":"Save new slot");
    }
}
