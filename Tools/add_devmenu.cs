// Adds DevMenu to the "Game" object in the scene being edited, if it isn't there yet.
var sess = Editor.SceneEditorSession.Active;
if ( sess is null ) return "no scene session";
var scene = sess.Scene;
var game = scene.GetAllObjects( true ).FirstOrDefault( o => o.Name == "Game" );
if ( game is null ) return "no Game object in " + scene.Name;
if ( game.Components.Get<DevMenu>() is not null ) return "Game already has DevMenu";
game.Components.Create<DevMenu>();
sess.HasUnsavedChanges = true;
sess.Save( false );
return $"DevMenu added to {game.Name} in {scene.Name}";
