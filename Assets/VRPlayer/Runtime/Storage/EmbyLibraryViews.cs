using System;

namespace VRPlayer
{
    public enum EmbyLibraryView { Poster=0, Banner=1, Disc=2, Logo=3, Thumb=4, List=5, Table=6 }

    [Serializable] public sealed class EmbyLibraryViewPreference
    {
        public string accountId, libraryId;
        public EmbyLibraryView view;
    }

    public readonly struct EmbyWallLayout
    {
        public readonly int Columns;
        public readonly float Width, ArtworkWidth, ArtworkHeight, CardHeight, Pitch;
        public EmbyWallLayout(int columns,float width,float artworkWidth,float artworkHeight,float cardHeight,float pitch)
        {Columns=columns;Width=width;ArtworkWidth=artworkWidth;ArtworkHeight=artworkHeight;CardHeight=cardHeight;Pitch=pitch;}
        public int PoolSize(int items,float viewport,float preload)=>Math.Min(items,(int)Math.Ceiling((viewport+preload*2)/Pitch+1)*Columns);
        public float ContentHeight(int items)=>(float)Math.Ceiling(items/(double)Columns)*Pitch;
    }

    public static class EmbyLibraryViews
    {
        public static EmbyLibraryView Normalize(EmbyLibraryView view)=>Enum.IsDefined(typeof(EmbyLibraryView),view)?view:EmbyLibraryView.Poster;
        public static string Label(EmbyLibraryView view)=>Normalize(view) switch {
            EmbyLibraryView.Banner=>"横幅图",EmbyLibraryView.Disc=>"光盘封面",EmbyLibraryView.Logo=>"徽标",
            EmbyLibraryView.Thumb=>"缩略图",EmbyLibraryView.List=>"列表",EmbyLibraryView.Table=>"表格",_=>"海报"
        };
        public static EmbyWallLayout Layout(EmbyLibraryView view)=>Normalize(view) switch {
            EmbyLibraryView.Banner=>new EmbyWallLayout(3,426,426,80,160,178),
            EmbyLibraryView.Disc=>new EmbyWallLayout(6,204,204,204,284,302),
            EmbyLibraryView.Logo=>new EmbyWallLayout(3,426,426,120,200,218),
            EmbyLibraryView.Thumb=>new EmbyWallLayout(4,314,314,314*9f/16,258,276),
            EmbyLibraryView.List=>new EmbyWallLayout(1,1314,84,126,150,162),
            EmbyLibraryView.Table=>new EmbyWallLayout(1,1314,84,84*9f/16,72,80),
            _=>new EmbyWallLayout(6,204,204,306,386,404)
        };
        public static EmbyWallLayout Layout(EmbyLibraryView view,float viewportWidth)
        {
            var original=Layout(view);float usable=Math.Max(204,viewportWidth-28);
            if(view==EmbyLibraryView.List||view==EmbyLibraryView.Table)return new EmbyWallLayout(1,usable,original.ArtworkWidth,original.ArtworkHeight,original.CardHeight,original.Pitch);
            int columns=Math.Max(1,(int)Math.Floor((usable+18)/(original.Width+18)));
            float width=(usable-(columns-1)*18)/columns;
            float artHeight=width*original.ArtworkHeight/original.ArtworkWidth;
            return new EmbyWallLayout(columns,width,width,artHeight,artHeight+80,artHeight+98);
        }
        public static EmbyWallLayout HomeLayout(float viewportWidth,bool thumbnails)
        {
            float usable=Math.Max(192,viewportWidth-28);
            int columns=Math.Max(1,(int)Math.Floor((usable+18)/(192+18)));
            float width=(usable-(columns-1)*18)/columns;
            float artHeight=thumbnails?width*9f/16:width*1.5f;
            return new EmbyWallLayout(columns,width,width,artHeight,artHeight+80,artHeight+98);
        }
        public static string ImageType(EmbyItem item,EmbyLibraryView view)
        {
            string requested=Normalize(view) switch {
                EmbyLibraryView.Banner=>"Banner",EmbyLibraryView.Disc=>"Disc",EmbyLibraryView.Logo=>"Logo",
                EmbyLibraryView.Thumb or EmbyLibraryView.Table=>"Thumb",_=>"Primary"
            };
            if(!string.IsNullOrEmpty(ImageTag(item,requested)))return requested;
            if(requested=="Thumb"&&!string.IsNullOrEmpty(ImageTag(item,"Backdrop")))return "Backdrop";
            return "Primary";
        }
        public static string ImageTag(EmbyItem item,string type)=>type switch {
            "Banner"=>item?.ImageTags?.Banner,"Disc"=>item?.ImageTags?.Disc,"Logo"=>item?.ImageTags?.Logo,
            "Thumb"=>item?.ImageTags?.Thumb,"Backdrop"=>item?.BackdropImageTags?.Length>0?item.BackdropImageTags[0]:null,
            _=>item?.ImageTags?.Primary
        };
    }
}
