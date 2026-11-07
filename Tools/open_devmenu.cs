// Test aid: force the dev menu open in the running game so it can be screenshotted.
var menu = Game.ActiveScene.GetAllComponents<DevMenu>().FirstOrDefault();
if ( menu is null ) return "no DevMenu in the active scene";
menu.SetOpen( true );
return "dev menu opened";
