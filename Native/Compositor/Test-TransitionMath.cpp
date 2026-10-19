#include "TransitionMath.h"
#include "FrameGate.h"
#include <cassert>
#include <iostream>
int main(){
    int checks=0;
    for(int width : {800,1920,2560,3840,7680})for(int sign : {-1,1}){
        const float distance=static_cast<float>(sign*width);
        const auto outgoing=deskpages::Slide(0,distance),incoming=deskpages::Slide(-distance,0);
        assert(std::abs(deskpages::Evaluate(outgoing,0))<0.001);checks++;
        assert(std::abs(deskpages::Evaluate(incoming,0)+distance)<0.001);checks++;
        assert(std::abs(deskpages::Evaluate(outgoing,deskpages::Duration)-distance)<0.02);checks++;
        assert(std::abs(deskpages::Evaluate(incoming,deskpages::Duration))<0.02);checks++;
        double prior=0;
        for(int sample=0;sample<=1000;sample++){
            double t=deskpages::Duration*sample/1000;
            double old=deskpages::Evaluate(outgoing,t),next=deskpages::Evaluate(incoming,t);
            assert(std::abs((old-next)-distance)<0.02);checks++;
            assert(old*sign>=prior-0.02);checks++;prior=old*sign;
        }
    }
    deskpages::TransitionState state;
    for(int sign : {-1,1})for(int i=0;i<deskpages::Segments;i++){
        auto c=deskpages::SmoothSegment(0,float(sign*3840),i);
        auto n=deskpages::SmoothSegment(float(-sign*3840),0,i);
        const double h=deskpages::Duration/deskpages::Segments;
        for(int j=0;j<=100;j++){
            double t=h*j/100;
            assert(std::abs((deskpages::Evaluate(c,t)-deskpages::Evaluate(n,t))-sign*3840)<0.02);checks++;
            assert(std::abs(deskpages::Evaluate(c,t)-sign*3840*deskpages::Ease((i+double(j)/100)/deskpages::Segments))<0.2);checks++;
        }
        if(i+1<deskpages::Segments){
            auto next=deskpages::SmoothSegment(0,float(sign*3840),i+1);
            assert(std::abs(deskpages::Evaluate(c,h)-next.constant)<0.02);checks++;
            double velocity=c.linear+2*c.quadratic*h+3*c.cubic*h*h;
            assert(std::abs(velocity-next.linear)<0.02);checks++;
        }
    }
    assert(deskpages::SmoothSegment(0,3840,0).linear==0);checks++;
    auto last=deskpages::SmoothSegment(0,3840,deskpages::Segments-1);
    double h=deskpages::Duration/deskpages::Segments;
    assert(std::abs(last.linear+2*last.quadratic*h+3*last.cubic*h*h)<0.02);checks++;
    std::vector<unsigned char> a(4096,255),b=a;
    assert(deskpages::UniformFrame(a));checks++;
    b[17]=0;assert(!deskpages::UniformFrame(b));checks++;b=a;
    assert(deskpages::SameFrame(a,b));checks++;
    b[17]=0;assert(!deskpages::SameFrame(a,b));checks++;
    b=a;b[17]=254;assert(deskpages::SameFrame(a,b));checks++;
    b=a;b[19]=0;assert(deskpages::SameFrame(a,b));checks++;
    b.pop_back();assert(!deskpages::SameFrame(a,b));checks++;
    assert(!deskpages::SameFrame({},{}));checks++;
    assert(!state.Play(true) && state.phase==deskpages::Phase::Idle);checks++;
    assert(!state.Begin(false) && state.phase==deskpages::Phase::Idle);checks++;
    assert(state.Begin(true) && state.phase==deskpages::Phase::Covering);checks++;
    assert(!state.Play(false) && state.phase==deskpages::Phase::Idle);checks++;
    assert(state.Begin(true) && state.Play(true) && state.phase==deskpages::Phase::Playing);checks++;
    state.Abort();assert(state.phase==deskpages::Phase::Idle);checks++;
    std::cout<<"PASS: "<<checks<<" transition curve / seamless offset / lifecycle assertions\n";
}
