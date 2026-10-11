using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Original still storyboards. Rectangles are measured source pixels, not assumed equal tiles.</summary>
    internal static class ClipCutStoryboard
    {
        private static readonly Vector2Int[] Sizes =
        {
            new Vector2Int(836,1881),new Vector2Int(1144,1375),new Vector2Int(1145,1374),
            new Vector2Int(1374,1145),new Vector2Int(1385,1136)
        };
        // Source rectangles use top-left coordinates. Small insets exclude generated divider strokes.
        private static readonly Rect[][] Frames =
        {
            PairRows(836,new[]{0,470,941,1411},new[]{463,933,1403,1881},new[]{416,416,416,416},new[]{419,419,419,419}),
            PairRows(1144,new[]{0,343,689,1033},new[]{334,680,1023,1375},new[]{570,570,570,570},new[]{574,574,574,574}),
            PairRows(1145,new[]{0,346,690,1033},new[]{338,680,1024,1374},new[]{568,568,568,568},new[]{575,575,575,575}),
            PairRows(1374,new[]{0,290,576,861},new[]{281,569,853,1145},new[]{682,682,682,682},new[]{692,692,692,692}),
            PairRows(1385,new[]{0,280,562,823},new[]{270,553,813,1136},new[]{687,686,685,684},new[]{697,696,695,694})
        };
        private static Rect[] PairRows(int width,int[] tops,int[] bottoms,int[] leftEnds,int[] rightStarts)
        {
            var result=new Rect[8];
            for(int row=0;row<4;row++)
            {
                result[row*2]=new Rect(2,tops[row]+2,leftEnds[row]-4,bottoms[row]-tops[row]-4);
                result[row*2+1]=new Rect(rightStarts[row]+2,tops[row]+2,width-rightStarts[row]-4,bottoms[row]-tops[row]-4);
            }
            return result;
        }
        internal static Texture2D[] Load()
        {
            var result=new Texture2D[Sizes.Length];
            for(int story=0;story<result.Length;story++)
            {
                var texture=Resources.Load<Texture2D>("UI/ClipCut/story-"+story);
                if(texture==null||texture.width!=Sizes[story].x||texture.height!=Sizes[story].y)
                    throw new InvalidOperationException("Clip Cut storyboard missing or imported at an unexpected size: "+story);
                result[story]=texture;
            }
            return result;
        }
        internal static void Show(Image image,Texture2D texture,int story,int frame)
        {
            if(story<0||story>=Frames.Length||frame<0||frame>=8)throw new ArgumentOutOfRangeException(nameof(frame));
            image.image=texture;image.sourceRect=Frames[story][frame];image.scaleMode=ScaleMode.ScaleToFit;
            image.MarkDirtyRepaint();
        }
    }
}
