/* Synthetic CPU validation of the exact shared library shipped to Unity. */
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

extern void *AssCreate(const unsigned char *,int,const unsigned char *,int);
extern int AssValidate(const unsigned char *,int);
extern int AssUpdate(void *,const unsigned char *,int);
extern int AssRender(void *,int64_t,int,int,int);
extern const unsigned char *AssPixels(void *);
extern void AssDestroy(void *);
#define CHECK(test) do { if (!(test)) { fprintf(stderr,"ASS smoke failed at line %d\n",__LINE__); return 1; } } while(0)
static const char *header="[Script Info]\nScriptType: v4.00+\nPlayResX: 640\nPlayResY: 360\n[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\nStyle: Default,Noto Sans CJK SC,48,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n";
static char script[8192];
static const char *make(const char *events){snprintf(script,sizeof(script),"%s%s",header,events);return script;}
static int occupied(const unsigned char *pixels){int sum=0;for(int i=0;i<640*360;i++)sum+=pixels[i*4+3]>0;return sum;}
static double center(const unsigned char *pixels){double sum=0,count=0;for(int y=0;y<360;y++)for(int x=0;x<640;x++){int alpha=pixels[(y*640+x)*4+3];sum+=x*alpha;count+=alpha;}return count?sum/count:0;}
int main(int argc,char **argv){
    CHECK(argc==2);FILE *file=fopen(argv[1],"rb");CHECK(file);fseek(file,0,SEEK_END);long length=ftell(file);rewind(file);
    unsigned char *font=malloc(length);CHECK(font);CHECK(fread(font,1,length,file)==(size_t)length);fclose(file);
    const char *text=make("Dialogue: 0,0:00:01.00,0:00:04.00,Default,,0,0,0,,{\\an7\\pos(0,0)\\clip(0,0,320,360)\\c&H0000FF&\\p1}m 0 0 l 640 0 640 360 0 360\n");
    CHECK(AssValidate((const unsigned char *)text,(int)strlen(text))==1);
    CHECK(AssValidate((const unsigned char *)"bad",3)==0);
    void *context=AssCreate((const unsigned char *)text,(int)strlen(text),font,(int)length);CHECK(context);
    CHECK(AssRender(context,0,640,360,1)==1);CHECK(occupied(AssPixels(context))==0);
    CHECK(AssRender(context,1500,640,360,0)==1);const unsigned char *pixels=AssPixels(context);
    int a=(180*640+100)*4,b=(180*640+500)*4;
    CHECK(pixels[a]>250&&pixels[a+1]==0&&pixels[a+2]==0&&pixels[a+3]>250);CHECK(pixels[b+3]==0);
    CHECK(AssRender(context,5000,640,360,0)==1);CHECK(occupied(AssPixels(context))==0);
    CHECK(AssRender(context,1500,640,360,0)==1);CHECK(occupied(AssPixels(context))>100000);
    CHECK(AssRender(context,1500,640,360,1)==1);CHECK(AssRender(context,1500,4096,360,1)==-1);
    text=make("Dialogue: 0,0:00:01.00,0:00:04.00,Default,,0,0,0,,{\\an7\\move(50,100,450,100)\\c&H0000FF&\\p1}m 0 0 l 80 0 80 60 0 60\n");
    CHECK(AssUpdate(context,(const unsigned char *)text,(int)strlen(text))==1);
    CHECK(AssRender(context,1500,640,360,1)==1);double left=center(AssPixels(context));
    CHECK(AssRender(context,3000,640,360,0)==1);CHECK(center(AssPixels(context))>left+150);
    text=make("Dialogue: 0,0:00:01.00,0:00:04.00,Default,,0,0,0,,{\\an7\\pos(0,0)\\fad(1000,1000)\\p1}m 0 0 l 200 0 200 100 0 100\n");
    CHECK(AssUpdate(context,(const unsigned char *)text,(int)strlen(text))==1);
    CHECK(AssRender(context,1100,640,360,1)==1);int faded=AssPixels(context)[(300*640+100)*4+3];
    CHECK(faded>0&&faded<60);CHECK(AssRender(context,2200,640,360,0)==1);CHECK(AssPixels(context)[(300*640+100)*4+3]>250);
    text=make("Dialogue: 0,0:00:01.00,0:00:04.00,Default,,0,0,0,,{\\an7\\pos(0,0)\\c&HFF0000&\\p1}m 0 0 l 200 0 200 100 0 100\nDialogue: 1,0:00:01.00,0:00:04.00,Default,,0,0,0,,{\\an7\\pos(0,0)\\c&H0000FF&\\alpha&H80&\\p1}m 0 0 l 200 0 200 100 0 100\n");
    CHECK(AssUpdate(context,(const unsigned char *)text,(int)strlen(text))==1);CHECK(AssRender(context,2000,640,360,1)==1);
    pixels=AssPixels(context)+(300*640+100)*4;CHECK(pixels[0]>120&&pixels[0]<135&&pixels[2]>120&&pixels[2]<135&&pixels[3]==255);
    text=make("Dialogue: 0,0:00:01.00,0:00:04.00,Default,,0,0,0,,{\\pos(100,100)}中文 ASS\n");
    CHECK(AssUpdate(context,(const unsigned char *)text,(int)strlen(text))==1);CHECK(AssRender(context,2000,640,360,1)==1);CHECK(occupied(AssPixels(context))>1000);
    AssDestroy(context);free(font);puts("{\"passed\":true,\"checks\":[\"libass parsing\",\"clipping\",\"RGBA channels\",\"end and backward seek\",\"forced refresh\",\"bounds\",\"move\",\"fade\",\"layer alpha composition\",\"Chinese fallback font\"]}");return 0;
}
