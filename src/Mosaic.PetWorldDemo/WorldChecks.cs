using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mosaic.Core;

namespace Mosaic.PetWorldDemo;

public static class WorldChecks
{
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var world=new WorldDirector();
        var previousBeat=-1;var timeAtBeat=0d;var minimumDistance=double.MaxValue;
        while(world.Cycle<1&&world.Time<1800)
        {
            world.Advance(.05);
            if(previousBeat!=world.BeatIndex){previousBeat=world.BeatIndex;timeAtBeat=world.Time;}
            Require(world.Time-timeAtBeat<90,$"Story stalled at beat {world.BeatIndex}: "+string.Join("; ",world.Actors.Select(a=>$"{a.Name} {a.Position} => {a.Destination}, route={a.Route.Count}")));
            foreach(var a in world.Actors)
            {
                Require(WorldRoom.Walkable(a.Position),$"{a.Name} crossed furniture at {a.Position} in beat {world.BeatIndex}.");
                foreach(var b in world.Actors.Where(b=>b.Id>a.Id))minimumDistance=Math.Min(minimumDistance,a.Position.Distance(b.Position));
            }
        }
        Require(world.Cycle==1,"Story never completed.");
        var fullCycleSeconds=world.Time;
        Require(minimumDistance>=.70,$"Actors overlapped: {minimumDistance}.");
        var automatic=world.Actors.ToDictionary(a=>a.Name,a=>a.SeenPoses.Count);
        foreach(var actor in world.Actors)
        {
            var missing=Enum.GetValues<PetPose>().Except(actor.SeenPoses).Where(p=>p is not(PetPose.Pat or PetPose.Drag)).ToArray();
            Require(missing.Length==0,$"{actor.Name} automatic coverage misses: {string.Join(", ",missing)}");
        }
        world.Paused=true;var frozen=world.Time;var frozenPositions=world.Actors.Select(a=>a.Position).ToArray();world.Advance(1);
        Require(world.Time==frozen&&world.Actors.Select(a=>a.Position).SequenceEqual(frozenPositions),"Pause changed simulation state.");
        world.Paused=false;
        for(var id=0;id<5;id++)
        {
            world.Interact(id,SceneAction.Pat);world.Advance(.1);Require(world.Actors[id].Pose(world.Time)==PetPose.Pat,"Interaction was overwritten by script.");
            var original=world.Actors[id].Position;world.BeginDrag(id);Require(world.Actors[id].Pose(world.Time)==PetPose.Drag,"Drag pose missing.");
            world.Drop(id,new(5,3));Require(WorldRoom.Walkable(world.Actors[id].Position),"Dropping on the desk did not snap to safe floor.");
            world.BeginDrag(id);world.Drop(id,original);
            for(var j=0;j<55;j++)world.Advance(.05);
        }
        foreach(var actor in world.Actors)Require(actor.SeenPoses.Count==Enum.GetValues<PetPose>().Length,$"{actor.Name} does not cover all poses.");
        foreach(var chapter in Enumerable.Range(0,6))
        {
            world.JumpChapter(chapter);Require(world.Current.Chapter==chapter&&!world.Actors.Any(a=>a.Dragging),"Chapter selection left stale state.");
        }
        var atSpeed=world.Time;world.Speed=4;world.Advance(.1);Require(Math.Abs(world.Time-atSpeed-.4)<.0001,"Playback speed mismatch.");
        var intervention=new WorldDirector();intervention.JumpChapter(2);intervention.Advance(.2);
        intervention.BeginDrag(0);intervention.Drop(0,intervention.Actors[1].Destination);
        Require(intervention.Actors[0].Position.Distance(intervention.Actors[1].Destination)>=.85,"A user drop occupied a reserved destination.");
        for(var tick=0;tick<2400&&intervention.Current.Chapter==2;tick++)intervention.Advance(.05);
        Require(intervention.Current.Chapter==3,"Story stalled after a user moved someone into the gathering area.");
        // Parse and render all 145 frames through the production sprite loader, then review representative scenes.
        var artWorld=new WorldDirector();var scene=new LibraryScene(artWorld);
        foreach(var actor in artWorld.Actors)
        {
            var sprite=new Mosaic.Host.Controls.WhalePetSprite();sprite.SetCharacter(actor.Character);
            foreach(var pose in Enum.GetValues<PetPose>()){sprite.SetPose(pose);Require(sprite.Source is BitmapSource b&&b.PixelWidth==512,"Missing sprite resource.");}
        }
        for(var chapter=0;chapter<6;chapter++)
        {
            artWorld.JumpChapter(chapter);
            for(var tick=0;tick<1600;tick++){artWorld.Advance(.05);if(!artWorld.Gathering&&artWorld.BeatElapsed>1.5)break;}
            if(chapter==2){artWorld.NextBeat();for(var tick=0;tick<45;tick++)artWorld.Advance(.05);}
            if(chapter==4){artWorld.NextBeat();for(var tick=0;tick<40;tick++)artWorld.Advance(.05);}
            if(chapter==5){artWorld.NextBeat();for(var tick=0;tick<1600;tick++){artWorld.Advance(.05);if(!artWorld.Gathering)break;}artWorld.NextBeat();artWorld.Advance(.1);}
            Capture(scene,Path.Combine(output,$"chapter-{chapter+1}.png"));
        }
        var window=new WorldWindow();
        Capture((FrameworkElement)window.Content,Path.Combine(output,"window-preview.png"));
        Require(window.World.Actors.Length==5,"Demo window did not initialize all five actors.");
        window.Close();
        File.WriteAllText(Path.Combine(output,"verification.json"),JsonSerializer.Serialize(new
        {
            passed=true,chapters=WorldDirector.Chapters.Length,beats=world.Story.Count,fullCycleSeconds,automaticPoses=automatic,
            withUserInteractions=world.Actors.ToDictionary(a=>a.Name,a=>a.SeenPoses.Count),minimumActorDistance=minimumDistance,
            checks=new[]{"full story completes without deadlock","furniture clearance","actor separation","27 automatic poses per actor","29 poses including real interactions","pause","script interruption priority","safe drag release","reserved destinations after user dragging","chapter reset","playback speed","145 production frames load","six scene renders","WPF window resources and initial layout"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }
    public static void Capture(FrameworkElement visual,string file)
    {
        visual.Measure(new(1400,900));visual.Arrange(new(0,0,1400,900));visual.UpdateLayout();visual.InvalidateVisual();visual.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1400,900,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(file);encoder.Save(stream);
    }
}
