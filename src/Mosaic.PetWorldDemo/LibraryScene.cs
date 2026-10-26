using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mosaic.Host.Controls;

namespace Mosaic.PetWorldDemo;

public sealed class LibraryScene : FrameworkElement
{
    private readonly WorldDirector _world;
    private readonly WhalePetSprite[] _sprites;
    private readonly Dictionary<string, SolidColorBrush> _brushes = [];
    private readonly Dictionary<string, FormattedText> _labels = [];
    private readonly Dictionary<(int, Mosaic.Core.PetPose), byte[]> _alpha = [];
    private readonly Dictionary<int, Rect> _actorBounds = [];
    private readonly DrawingGroup _background;
    private readonly List<(double Depth, DrawingGroup Drawing)> _furniture = [];
    private Point? _pressed;
    private FloorPoint _dragOrigin;
    private int? _held;
    private bool _dragging;
    public event Action? SelectionChanged;
    public LibraryScene(WorldDirector world)
    {
        _world = world; Width = 1400; Height = 900; Focusable = true;
        _sprites = world.Actors.Select(a => { var sprite = new WhalePetSprite(); sprite.SetCharacter(a.Character); return sprite; }).ToArray();
        _background = new(); using (var dc = _background.Open()) DrawRoom(dc); _background.Freeze();
        foreach (var item in WorldRoom.Objects)
        {
            var group = new DrawingGroup(); using (var dc = group.Open()) DrawFurniture(dc, item); group.Freeze();
            _furniture.Add((item.X + item.Y + (item.Width + item.Depth) * .65, group));
        }
    }
    public static Point Project(FloorPoint p, double height = 0) => new(600 + (p.X - p.Y) * 53, 230 + (p.X + p.Y) * 25 - height);
    public static FloorPoint Unproject(Point p) => new(((p.X - 600) / 53 + (p.Y - 230) / 25) / 2, ((p.Y - 230) / 25 - (p.X - 600) / 53) / 2);
    private Point P(double x, double y, double z = 0) => Project(new(x, y), z);
    private SolidColorBrush C(string color)
    { if (!_brushes.TryGetValue(color, out var brush)) { brush = new((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); _brushes[color] = brush; } return brush; }
    private static void Polygon(DrawingContext dc, Brush brush, params Point[] vertices)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open()) { g.BeginFigure(vertices[0], true, true); g.PolyLineTo(vertices.Skip(1).ToArray(), true, false); }
        geometry.Freeze(); dc.DrawGeometry(brush, null, geometry);
    }
    private void Quad(DrawingContext dc, string color, double x, double y, double w, double d, double z = 0)
        => Polygon(dc, C(color), P(x,y,z), P(x+w,y,z), P(x+w,y+d,z), P(x,y+d,z));
    private void Box(DrawingContext dc, double x, double y, double w, double d, double height, string top, string left, string right, double bottom = 0)
    {
        Polygon(dc, C(left), P(x,y+d,bottom), P(x+w,y+d,bottom), P(x+w,y+d,height), P(x,y+d,height));
        Polygon(dc, C(right), P(x+w,y,bottom), P(x+w,y+d,bottom), P(x+w,y+d,height), P(x+w,y,height));
        Quad(dc, top, x,y,w,d,height);
    }
    private void Label(DrawingContext dc, string text, Point point, double size, string color, bool bold = false)
    {
        var key = text + size + color + bold;
        if (!_labels.TryGetValue(key, out var formatted))
        {
            formatted = new(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, C(color), 1);
            if (_labels.Count > 400) _labels.Clear(); _labels[key] = formatted;
        }
        dc.DrawText(formatted, point);
    }
    private void DrawRoom(DrawingContext dc)
    {
        dc.DrawRectangle(C("#EEE9DE"), null, new(0,0,1400,900));
        dc.DrawEllipse(C("#170E251B"), null, new(688,671), 530,115);
        // Two walls, the floor and visible cutaway thickness share the same isometric projection.
        Polygon(dc,C("#D8D9C7"),P(0,0),P(12,0),P(12,0,215),P(0,0,215));
        Polygon(dc,C("#E4DDCC"),P(0,0),P(0,9),P(0,9,215),P(0,0,215));
        for (var x=0;x<12;x++) for(var y=0;y<9;y++)
            Quad(dc,(x+y)%3==0?"#C8B492":(x+y)%3==1?"#CEBA98":"#D3BE9D",x,y,1,1);
        Polygon(dc,C("#AB9273"),P(0,9),P(12,9),P(12,9,-14),P(0,9,-14));
        Polygon(dc,C("#927D63"),P(12,0),P(12,9),P(12,9,-14),P(12,0,-14));
        // Wall skirting, oak cap and a quiet lower panel.
        Polygon(dc,C("#A58C69"),P(0,0),P(0,9),P(0,9,13),P(0,0,13));
        Polygon(dc,C("#9C886A"),P(0,0),P(12,0),P(12,0,13),P(0,0,13));
        Box(dc,0,0,12,.12,220,"#B9A889","#AC9876","#A28E70",210);
        Box(dc,0,0,.12,9,220,"#CBBBA0","#B49E7D","#AE9879",210);
        // Tall greenhouse window on the back wall.
        Polygon(dc,C("#7D897A"),P(2.2,.01,55),P(7.5,.01,55),P(7.5,.01,196),P(2.2,.01,196));
        Polygon(dc,C("#C6D7C9"),P(2.35,.03,64),P(7.35,.03,64),P(7.35,.03,187),P(2.35,.03,187));
        Polygon(dc,C("#9FB8A2"),P(2.35,.04,64),P(7.35,.04,64),P(7.35,.04,121),P(6.2,.04,134),P(4.8,.04,109),P(3.1,.04,137),P(2.35,.04,126));
        foreach(var x in new[]{2.35,4.0,5.65,7.35})
            Polygon(dc,C("#798776"),P(x,.06,64),P(x+.08,.06,64),P(x+.08,.06,187),P(x,.06,187));
        Polygon(dc,C("#798776"),P(2.35,.06,126),P(7.4,.06,126),P(7.4,.06,131),P(2.35,.06,131));
        // Window light and a woven rug remain below the people and furniture.
        Quad(dc,"#35FFF2C5",3.2,1.2,3.8,3.9);
        Quad(dc,"#A9AD8C",4.3,4.3,4.2,3.5,1);
        Quad(dc,"#C3C2A1",4.45,4.45,3.9,3.2,1.1);
        Quad(dc,"#B4B795",4.58,4.58,3.64,2.94,1.2);
        for(var i=0;i<14;i++)
        {
            var a=P(4.4+i*.3,7.8);var b=P(4.4+i*.3,7.95);
            dc.DrawLine(new(C("#D8D0B4"),2),a,b);
        }
        // Five low floor cushions for the rest chapter (no invented sitting animation).
        foreach(var (x,y) in new[]{(4.5,8d),(7d,8d),(9.5,7d),(2d,7.5),(9.5,4.5)})
        {
            Box(dc,x-.36,y-.3,.72,.6,6,"#CFB9A4","#B59980","#BBA58E");
            Quad(dc,"#E2CDB4",x-.27,y-.22,.54,.44,6.2);
        }
        // Framed botanical print on the left wall.
        Polygon(dc,C("#978A6F"),P(.03,5.4,75),P(.03,7.4,75),P(.03,7.4,174),P(.03,5.4,174));
        Polygon(dc,C("#F3ECDD"),P(.07,5.55,82),P(.07,7.25,82),P(.07,7.25,167),P(.07,5.55,167));
        var stem=P(.10,6.4,91);dc.DrawLine(new(C("#7D927C"),3),stem,P(.10,6.4,151));
        foreach(var (y,z) in new[]{(6.13,112d),(6.67,125d),(6.2,140d)}) dc.DrawEllipse(C("#9EAE8D"),null,P(.12,y,z),10,5);
        Label(dc,"窗边的第五个座位",new(90,775),22,"#716A58",true);
        Label(dc,"一页书，两杯茶，五个不太安静的朋友。",new(90,813),14,"#8C8572");
        Label(dc,"LIBRARY / AFTERNOON",new(1060,790),12,"#8C8572");
    }
    private void DrawFurniture(DrawingContext dc, RoomObject o)
    {
        var (x,y,w,d,h)=(o.X,o.Y,o.Width,o.Depth,o.Height);
        Quad(dc,"#1A3B291B",x+.15,y+.15,w,d);
        if(o.Kind=="shelf")
        {
            Box(dc,x,y,w,d,h,"#A88F68","#B39A74","#8D7658");
            // The x-facing side of each shelf is visible to the room.
            for(var level=0;level<3;level++)
            {
                var z=12+level*(h-12)/3;
                Polygon(dc,C("#655B47"),P(x+w+.01,y+.1,z),P(x+w+.01,y+d-.1,z),P(x+w+.01,y+d-.1,z+34),P(x+w+.01,y+.1,z+34));
                for(var book=0;book<(int)(d*9)-2;book++)
                {
                    var by=y+.14+book*.11;var bh=23+(book*7+level*3)%11;
                    var colors=new[]{"#A5AB8B","#D6C6A3","#AE8067","#82959C","#C5B190"};
                    Polygon(dc,C(colors[(book+level)%5]),P(x+w+.03,by,z+2),P(x+w+.03,by+.08,z+2),P(x+w+.03,by+.08,z+bh),P(x+w+.03,by,z+bh));
                }
            }
        }
        else if(o.Kind is "desk" or "table")
        {
            foreach(var (lx,ly) in new[]{(x+.12,y+.1),(x+w-.23,y+.1),(x+.12,y+d-.21),(x+w-.23,y+d-.21)})
                Box(dc,lx,ly,.11,.11,h-5,"#8C7456","#816B50","#6E5C47");
            Box(dc,x,y,w,d,h,"#D6BC91","#AD8D65","#997D5D",h-7);
            if(o.Kind=="desk")
            {
                Quad(dc,"#EEE6D3",x+.4,y+.2,.75,.6,h+.5);
                Quad(dc,"#C4B792",x+1.18,y+.2,.55,.6,h+.5);
                Quad(dc,"#AD8B68",x+1.88,y+.25,.25,.4,h+2);
            }
            else
            {
                dc.DrawEllipse(C("#F3EBDD"),new(C("#B39F7F"),2),P(x+.55,y+.42,h+3),16,7);
                dc.DrawEllipse(C("#CCAD72"),null,P(x+.55,y+.42,h+4),10,4);
                Cup(dc,P(x+1.3,y+.4,h+1));
            }
        }
        else if(o.Kind=="tea")
        {
            Box(dc,x,y,w,d,h,"#D9C9A8","#9EAA8D","#7E8F78");
            Box(dc,x-.04,y-.02,w+.08,d+.04,h+5,"#E7D7BA","#BEAA86","#B09B77",h);
            for(var i=0;i<3;i++)
            {
                var cx=x+.35+i*.78;
                Polygon(dc,C("#CAD1B9"),P(cx,y+d+.01,14),P(cx+.5,y+d+.01,14),P(cx+.5,y+d+.01,43),P(cx,y+d+.01,43));
                dc.DrawEllipse(C("#7D8167"),null,P(cx+.25,y+d+.03,36),2.5,2.5);
            }
            Cup(dc,P(x+.5,y+.5,h+8));Cup(dc,P(x+.95,y+.5,h+8));
            var kettle=P(x+1.8,y+.4,h+8);dc.DrawEllipse(C("#758E80"),null,new(kettle.X,kettle.Y-10),13,16);
            dc.DrawEllipse(null,new(C("#6A8376"),4),new(kettle.X+12,kettle.Y-12),8,9);
        }
        else
        {
            Box(dc,x+.06,y+.08,w-.12,d-.16,28,"#B3A58A","#BCAC8E","#A39379");
            var center=P(x+w/2,y+d/2,27);
            dc.DrawLine(new(C("#7F8C68"),4),center,new(center.X,center.Y-h*.65));
            for(var n=0;n<7;n++)
            { var angle=n*2.4;var pt=new Point(center.X+Math.Cos(angle)*19,center.Y-18-n*7);dc.DrawEllipse(C(n%2==0?"#829771":"#98AC80"),null,pt,16,10); }
        }
    }
    private void Cup(DrawingContext dc,Point p)
    {dc.DrawEllipse(C("#E9E3D2"),null,new(p.X,p.Y+1),11,4);dc.DrawRoundedRectangle(C("#F5EDDA"),new(C("#B5A78B"),1),new(p.X-7,p.Y-11,14,12),3,3);dc.DrawEllipse(C("#A88965"),null,new(p.X,p.Y-10),6,2.5);}
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawDrawing(_background);
        _actorBounds.Clear();
        var layers = _furniture.Select(f => (f.Depth, Actor:(WorldActor?)null, Drawing:(DrawingGroup?)f.Drawing))
            .Concat(_world.Actors.Select(a => (a.Dragging?100:a.Position.X+a.Position.Y,Actor:(WorldActor?)a,Drawing:(DrawingGroup?)null))).OrderBy(l=>l.Item1);
        foreach(var layer in layers)
            if(layer.Actor is WorldActor actor) DrawActor(dc,actor); else dc.DrawDrawing(layer.Drawing!);
        if(_world.Speaker is int id && _world.Speech.Length>0) DrawSpeech(dc,_world.Actors[id],_world.Speech);
    }
    private void DrawActor(DrawingContext dc,WorldActor actor)
    {
        var foot=Project(actor.Position);var size=175+(actor.Position.X+actor.Position.Y)*1.9;
        if(actor.Dragging) foot.Y-=24;
        var breathe=actor.Moving||actor.Dragging?0:Math.Sin(_world.Time*1.7+actor.Id)*.75;
        dc.DrawEllipse(C(actor.Id==_world.SelectedActor?"#45718570":"#253F392B"),null,Project(actor.Position),size*.14,size*.045);
        if(actor.Id==_world.SelectedActor)dc.DrawEllipse(null,new(C("#809B886A"),2),Project(actor.Position),size*.18,size*.06);
        var rect=new Rect(foot.X-size/2,foot.Y-size*480/512+breathe,size,size);
        var pose=actor.Pose(_world.Time);_sprites[actor.Id].SetPose(pose);dc.DrawImage(_sprites[actor.Id].Source,rect);_actorBounds[actor.Id]=rect;
        Label(dc,actor.Name,new(foot.X-actor.Name.Length*3.6,foot.Y+15),12,"#655F51",actor.Id==_world.SelectedActor);
    }
    private void DrawSpeech(DrawingContext dc,WorldActor actor,string line)
    {
        var rect=_actorBounds[actor.Id];var width=285d;
        var text=new FormattedText(line,CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),15,C("#443E33"),1){MaxTextWidth=width-30,LineHeight=24};
        var height=text.Height+46;var left=Math.Clamp(rect.X+rect.Width/2-width/2,35,1365-width);var top=Math.Clamp(rect.Top-height-4,25,730-height);
        dc.DrawRoundedRectangle(C("#19000000"),null,new(left+3,top+5,width,height),12,12);
        dc.DrawRoundedRectangle(C("#FFFDF6"),new(C("#D8CCB7"),1),new(left,top,width,height),12,12);
        Polygon(dc,C("#FFFDF6"),new(left+width/2-8,top+height-1),new(left+width/2+8,top+height-1),new(left+width/2,top+height+9));
        Label(dc,actor.Name,new(left+15,top+10),12,"#8A7757",true);dc.DrawText(text,new(left+15,top+31));
    }
    private int? HitActor(Point point)
    {
        foreach(var actor in _world.Actors.OrderByDescending(a=>a.Position.X+a.Position.Y))
        {
            if(!_actorBounds.TryGetValue(actor.Id,out var rect)||!rect.Contains(point))continue;
            var pose=actor.Pose(_world.Time);var key=(actor.Id,pose);
            if(!_alpha.TryGetValue(key,out var data))
            {
                _sprites[actor.Id].SetPose(pose);var bitmap=new FormatConvertedBitmap((BitmapSource)_sprites[actor.Id].Source,PixelFormats.Bgra32,null,0);
                data=new byte[512*512*4];bitmap.CopyPixels(data,512*4,0);_alpha[key]=data;
            }
            var x=Math.Clamp((int)((point.X-rect.X)/rect.Width*512),0,511);var y=Math.Clamp((int)((point.Y-rect.Y)/rect.Height*512),0,511);
            if(data[(y*512+x)*4+3]>24)return actor.Id;
        }
        return null;
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);var point=e.GetPosition(this);var id=HitActor(point);if(id is null)return;
        _world.SelectedActor=id.Value;SelectionChanged?.Invoke();
        if(e.ClickCount==2){_world.Interact(id.Value,SceneAction.Pat);InvalidateVisual();return;}
        _held=id;_pressed=point;_dragOrigin=_world.Actors[id.Value].Position;CaptureMouse();e.Handled=true;InvalidateVisual();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);if(_held is not int id||_pressed is not Point start)return;var now=e.GetPosition(this);
        if(!_dragging&&(now-start).Length>7){_dragging=true;_world.BeginDrag(id);}
        if(_dragging){var origin=Project(_dragOrigin);_world.Actors[id].Position=Unproject(origin+(now-start));InvalidateVisual();}
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){FinishDrag();ReleaseMouseCapture();base.OnMouseLeftButtonUp(e);}
    protected override void OnLostMouseCapture(MouseEventArgs e){FinishDrag();base.OnLostMouseCapture(e);}
    private void FinishDrag()
    {
        if(_dragging&&_held is int id)_world.Drop(id,_world.Actors[id].Position);
        _held=null;_pressed=null;_dragging=false;SelectionChanged?.Invoke();InvalidateVisual();
    }
}
