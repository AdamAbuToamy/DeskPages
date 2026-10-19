#pragma once
#include <algorithm>
#include <cmath>

namespace deskpages {
constexpr double Duration=0.380;
constexpr int Segments=16;
struct Cubic {float constant,linear,quadratic,cubic;};
inline Cubic Slide(float from,float to,double duration=Duration) {
    const double delta=to-from;
    return {from,0.0f,static_cast<float>(3*delta/(duration*duration)),
        static_cast<float>(-2*delta/(duration*duration*duration))};
}
inline double Evaluate(Cubic curve,double t) {
    return curve.constant+curve.linear*t+curve.quadratic*t*t+curve.cubic*t*t*t;
}
inline double Ease(double u){return u*u*u*(10+u*(-15+6*u));}
inline double EaseVelocity(double u){return 30*u*u*(1-u)*(1-u);}
// Hermite pieces follow quintic easing, including zero endpoint velocity.
inline Cubic SmoothSegment(float from,float to,int index) {
    const double h=Duration/Segments,u=double(index)/Segments,v=double(index+1)/Segments;
    const double delta=to-from,a=from+delta*Ease(u),b=from+delta*Ease(v);
    const double va=delta*EaseVelocity(u)/Duration,vb=delta*EaseVelocity(v)/Duration;
    return {float(a),float(va),float((3*(b-a)/h-2*va-vb)/h),float((2*(a-b)/h+va+vb)/(h*h))};
}
enum class Phase {Idle,Covering,Playing};
struct TransitionState {
    Phase phase=Phase::Idle;
    bool Begin(bool surfacePresented) {phase=surfacePresented?Phase::Covering:Phase::Idle;return surfacePresented;}
    bool Play(bool targetReady) {
        if(phase!=Phase::Covering || !targetReady){phase=Phase::Idle;return false;}
        phase=Phase::Playing;return true;
    }
    void Abort(){phase=Phase::Idle;}
};
}
