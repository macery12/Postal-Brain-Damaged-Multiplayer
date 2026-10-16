using Hyperstrange.PBD.Contraptions;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace PBDMP.Plugin;

/// <summary>Scene authorship survives session leave/rehost, but never map unload.</summary>
internal static class WorldAuthoredProps
{
    private static readonly Dictionary<int,HashSet<int>> Scenes=new();
    private static UnityAction<Scene,LoadSceneMode>? _loaded;
    private static UnityAction<Scene>? _unloaded;
    internal static void Install()
    {
        if(_loaded!=null) return;
        _loaded=(System.Action<Scene,LoadSceneMode>)((scene,_)=>Capture(scene));
        _unloaded=(System.Action<Scene>)(scene=>Scenes.Remove(scene.handle));
        UnityEngine.SceneManagement.SceneManager.sceneLoaded+=_loaded;
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded+=_unloaded;
        for(var i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++) Capture(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i));
    }
    private static void Capture(Scene scene)
    {
        if(!scene.IsValid() || !scene.isLoaded) return;
        var authored=new HashSet<int>();
        foreach(var root in scene.GetRootGameObjects())
            foreach(var physical in root.GetComponentsInChildren<PhysicalObject>(true))
                if(WorldCatalogue.SafeMotion(physical.transform) && physical.OriginalPoolable==null) authored.Add(physical.GetInstanceID());
        Scenes[scene.handle]=authored;
    }
    internal static bool Contains(PhysicalObject prop)=>Scenes.TryGetValue(prop.gameObject.scene.handle,out var authored) && authored.Contains(prop.GetInstanceID());
}
