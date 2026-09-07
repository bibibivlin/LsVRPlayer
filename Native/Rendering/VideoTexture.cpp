#include <jni.h>
#include <GLES3/gl3.h>
#include <GLES2/gl2ext.h>
#include <android/log.h>
#include <atomic>
#include <cstdint>

namespace {
JavaVM* vm=nullptr;
jclass bridge=nullptr;
jobject surfaceTexture=nullptr, surface=nullptr;
jmethodID updateImage=nullptr, getTransform=nullptr;
jfloatArray matrixArray=nullptr;
GLuint externalTexture=0, outputTexture=0, fbo=0, program=0, vao=0;
std::atomic<int> requestedWidth{1280}, requestedHeight{720}, textureId{0}, epoch{0}, frames{0};
std::atomic<bool> frameReady{false};
int width=0,height=0;
void log(const char* message){__android_log_print(ANDROID_LOG_ERROR,"VRPlayerRender","%s",message);}
JNIEnv* environment(){JNIEnv* env=nullptr; if(!vm)return nullptr; if(vm->GetEnv((void**)&env,JNI_VERSION_1_6)!=JNI_OK) vm->AttachCurrentThread(&env,nullptr); return env;}
bool exception(JNIEnv* env){if(!env->ExceptionCheck())return false; env->ExceptionDescribe();env->ExceptionClear();log("JNI rendering exception");return true;}
GLuint shader(GLenum type,const char* source){GLuint result=glCreateShader(type);glShaderSource(result,1,&source,nullptr);glCompileShader(result); GLint ok;glGetShaderiv(result,GL_COMPILE_STATUS,&ok);if(!ok){char out[1024];glGetShaderInfoLog(result,1024,nullptr,out);log(out);}return result;}
struct State {
    GLint framebuffer,viewport[4],currentProgram,vertexArray,activeTexture,tex2d,texExternal,sampler;
    GLboolean blend,depth,cull,scissor,stencil,mask[4];
    State(){
        glGetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING,&framebuffer);glGetIntegerv(GL_VIEWPORT,viewport);glGetIntegerv(GL_CURRENT_PROGRAM,&currentProgram);glGetIntegerv(GL_VERTEX_ARRAY_BINDING,&vertexArray);
        glGetIntegerv(GL_ACTIVE_TEXTURE,&activeTexture);glActiveTexture(GL_TEXTURE0);glGetIntegerv(GL_TEXTURE_BINDING_2D,&tex2d);glGetIntegerv(GL_TEXTURE_BINDING_EXTERNAL_OES,&texExternal);glGetIntegerv(GL_SAMPLER_BINDING,&sampler);
        blend=glIsEnabled(GL_BLEND);depth=glIsEnabled(GL_DEPTH_TEST);cull=glIsEnabled(GL_CULL_FACE);scissor=glIsEnabled(GL_SCISSOR_TEST);stencil=glIsEnabled(GL_STENCIL_TEST);glGetBooleanv(GL_COLOR_WRITEMASK,mask);
        glDisable(GL_BLEND);glDisable(GL_DEPTH_TEST);glDisable(GL_CULL_FACE);glDisable(GL_SCISSOR_TEST);glDisable(GL_STENCIL_TEST);glColorMask(GL_TRUE,GL_TRUE,GL_TRUE,GL_TRUE);glBindSampler(0,0);
    }
    static void enabled(GLenum item,bool on){if(on)glEnable(item);else glDisable(item);}
    ~State(){
        glBindFramebuffer(GL_DRAW_FRAMEBUFFER,framebuffer);glViewport(viewport[0],viewport[1],viewport[2],viewport[3]);glUseProgram(currentProgram);glBindVertexArray(vertexArray);
        glActiveTexture(GL_TEXTURE0);glBindTexture(GL_TEXTURE_2D,tex2d);glBindTexture(GL_TEXTURE_EXTERNAL_OES,texExternal);glBindSampler(0,sampler);glActiveTexture(activeTexture);
        enabled(GL_BLEND,blend);enabled(GL_DEPTH_TEST,depth);enabled(GL_CULL_FACE,cull);enabled(GL_SCISSOR_TEST,scissor);enabled(GL_STENCIL_TEST,stencil);glColorMask(mask[0],mask[1],mask[2],mask[3]);
    }
};
void releaseJava(JNIEnv* env){
    if(!env)return;
    if(surface){jclass type=env->GetObjectClass(surface);env->CallVoidMethod(surface,env->GetMethodID(type,"release","()V"));env->DeleteLocalRef(type);env->DeleteGlobalRef(surface);surface=nullptr;}
    if(surfaceTexture){jclass type=env->GetObjectClass(surfaceTexture);env->CallVoidMethod(surfaceTexture,env->GetMethodID(type,"release","()V"));env->DeleteLocalRef(type);env->DeleteGlobalRef(surfaceTexture);surfaceTexture=nullptr;}
    if(matrixArray){env->DeleteGlobalRef(matrixArray);matrixArray=nullptr;}exception(env);
}
bool initialize(JNIEnv* env){
    if(!bridge)return false;
    releaseJava(env);
    glGenTextures(1,&externalTexture);glBindTexture(GL_TEXTURE_EXTERNAL_OES,externalTexture);
    glTexParameteri(GL_TEXTURE_EXTERNAL_OES,GL_TEXTURE_MIN_FILTER,GL_LINEAR);glTexParameteri(GL_TEXTURE_EXTERNAL_OES,GL_TEXTURE_MAG_FILTER,GL_LINEAR);glTexParameteri(GL_TEXTURE_EXTERNAL_OES,GL_TEXTURE_WRAP_S,GL_CLAMP_TO_EDGE);glTexParameteri(GL_TEXTURE_EXTERNAL_OES,GL_TEXTURE_WRAP_T,GL_CLAMP_TO_EDGE);
    jclass st=env->FindClass("android/graphics/SurfaceTexture");jobject localSt=env->NewObject(st,env->GetMethodID(st,"<init>","(I)V"),(jint)externalTexture);surfaceTexture=env->NewGlobalRef(localSt);env->DeleteLocalRef(localSt);
    updateImage=env->GetMethodID(st,"updateTexImage","()V");getTransform=env->GetMethodID(st,"getTransformMatrix","([F)V");env->DeleteLocalRef(st);
    jfloatArray localArray=env->NewFloatArray(16);matrixArray=(jfloatArray)env->NewGlobalRef(localArray);env->DeleteLocalRef(localArray);
    jclass surfaceClass=env->FindClass("android/view/Surface");jobject localSurface=env->NewObject(surfaceClass,env->GetMethodID(surfaceClass,"<init>","(Landroid/graphics/SurfaceTexture;)V"),surfaceTexture);surface=env->NewGlobalRef(localSurface);env->DeleteLocalRef(localSurface);env->DeleteLocalRef(surfaceClass);
    env->CallStaticVoidMethod(bridge,env->GetStaticMethodID(bridge,"setVideoSurface","(Landroid/view/Surface;Landroid/graphics/SurfaceTexture;)V"),surface,surfaceTexture);
    if(exception(env))return false;
    const char* vert="#version 300 es\nprecision highp float; out vec2 uv; void main(){vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);uv=p;gl_Position=vec4(p*2.0-1.0,0,1);}";
    const char* frag="#version 300 es\n#extension GL_OES_EGL_image_external_essl3 : require\nprecision highp float; uniform samplerExternalOES video; uniform mat4 transform; in vec2 uv; out vec4 color; void main(){color=vec4(texture(video,(transform*vec4(uv,0,1)).xy).rgb,1);}";
    GLuint v=shader(GL_VERTEX_SHADER,vert),f=shader(GL_FRAGMENT_SHADER,frag);program=glCreateProgram();glAttachShader(program,v);glAttachShader(program,f);glLinkProgram(program);glDeleteShader(v);glDeleteShader(f);
    glGenVertexArrays(1,&vao);glGenFramebuffers(1,&fbo);glGenTextures(1,&outputTexture);width=height=0;textureId=outputTexture;epoch++;frames=0;
    __android_log_print(ANDROID_LOG_INFO,"VRPlayerRender","GPU Surface initialized (texture %u)",outputTexture);return true;
}
void render(int event){
    JNIEnv* env=environment();if(!env)return;
    State state;
    if(event==2){releaseJava(env);if(outputTexture)glDeleteTextures(1,&outputTexture);if(externalTexture)glDeleteTextures(1,&externalTexture);if(program)glDeleteProgram(program);if(fbo)glDeleteFramebuffers(1,&fbo);if(vao)glDeleteVertexArrays(1,&vao);outputTexture=externalTexture=program=fbo=vao=0;textureId=0;epoch++;return;}
    if(!outputTexture || !glIsTexture(outputTexture)){if(!initialize(env))return;}
    int newWidth=requestedWidth.load(),newHeight=requestedHeight.load();
    glBindTexture(GL_TEXTURE_2D,outputTexture);
    if(width!=newWidth||height!=newHeight){
        width=newWidth;height=newHeight;glTexImage2D(GL_TEXTURE_2D,0,GL_RGBA8,width,height,0,GL_RGBA,GL_UNSIGNED_BYTE,nullptr);
        glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_MIN_FILTER,GL_LINEAR);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_MAG_FILTER,GL_LINEAR);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_WRAP_S,GL_CLAMP_TO_EDGE);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_WRAP_T,GL_CLAMP_TO_EDGE);
        glBindFramebuffer(GL_DRAW_FRAMEBUFFER,fbo);glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER,GL_COLOR_ATTACHMENT0,GL_TEXTURE_2D,outputTexture,0);
        GLfloat clear[4]={0,0,0,1};glClearBufferfv(GL_COLOR,0,clear);
    }
    if(event==3){glBindFramebuffer(GL_DRAW_FRAMEBUFFER,fbo);GLfloat clear[4]={0,0,0,1};glClearBufferfv(GL_COLOR,0,clear);return;}
    if(!frameReady.exchange(false))return;
    env->CallVoidMethod(surfaceTexture,updateImage);if(exception(env))return;
    env->CallVoidMethod(surfaceTexture,getTransform,matrixArray);jfloat matrix[16];env->GetFloatArrayRegion(matrixArray,0,16,matrix);
    glBindFramebuffer(GL_DRAW_FRAMEBUFFER,fbo);glViewport(0,0,width,height);glUseProgram(program);glBindVertexArray(vao);glBindTexture(GL_TEXTURE_EXTERNAL_OES,externalTexture);
    glUniform1i(glGetUniformLocation(program,"video"),0);glUniformMatrix4fv(glGetUniformLocation(program,"transform"),1,GL_FALSE,matrix);glDrawArrays(GL_TRIANGLES,0,3);frames++;
}
}
extern "C" {
JNIEXPORT void JNICALL Java_com_linxyi_vrplayer_PlayerBridge_nativeBootstrap(JNIEnv* env,jclass type){env->GetJavaVM(&vm);if(!bridge)bridge=(jclass)env->NewGlobalRef(type);}
JNIEXPORT void JNICALL Java_com_linxyi_vrplayer_PlayerBridge_nativeFrameAvailable(JNIEnv*,jclass){frameReady=true;}
__attribute__((visibility("default"))) void* GetRenderEventFunc(){return (void*)render;}
__attribute__((visibility("default"))) int GetVideoTexture(){return textureId.load();}
__attribute__((visibility("default"))) int GetTextureEpoch(){return epoch.load();}
__attribute__((visibility("default"))) int GetRenderedFrames(){return frames.load();}
__attribute__((visibility("default"))) void SetVideoSize(int w,int h){if(w>0&&h>0&&w<=8192&&h<=8192){requestedWidth=w;requestedHeight=h;}}
}
