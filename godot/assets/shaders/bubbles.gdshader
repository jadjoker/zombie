// GPU bubbles (s&box WaterBubble behaviour on the GPU). Each one is emitted from code with
// CUSTOM = ( size in inches, life in seconds, delay in seconds, 0 ): it waits out the delay, then rises faster as it
// goes, wobbling and drifting, grows in quickly and pops at the end. Particles not emitted from code are dropped.
shader_type particles;

const float IN = 0.0254;

float hash( float n ) { return fract( sin( n ) * 43758.5453 ); }

void start()
{
	if ( !RESTART_CUSTOM ) { ACTIVE = false; }
	else
	{
		CUSTOM.w = 0.0;
		float seed = float( INDEX ) * 1.618 + TIME;
		// a sideways drift, fixed for this bubble (s&box Rng.InSphere( 6 ).Flat())
		COLOR = vec4( ( hash( seed ) - 0.5 ) * 12.0, hash( seed + 3.1 ) * 6.283, ( hash( seed + 7.7 ) - 0.5 ) * 12.0, 1.0 );
		TRANSFORM[0].xyz = vec3( 0.0001, 0.0, 0.0 );
		TRANSFORM[1].xyz = vec3( 0.0, 0.0001, 0.0 );
		TRANSFORM[2].xyz = vec3( 0.0, 0.0, 0.0001 );
		VELOCITY = vec3( 0.0 );
	}
}

void process()
{
	CUSTOM.w += DELTA;
	float age = CUSTOM.w - CUSTOM.z;
	if ( age < 0.0 ) { VELOCITY = vec3( 0.0 ); }
	else
	{
		float t = age / max( CUSTOM.y, 0.01 );
		if ( t >= 1.0 ) { ACTIVE = false; }
		float rise = 38.0 + 40.0 * t;
		float phase = COLOR.g;
		vec3 wobble = vec3( sin( age * 4.0 + phase ), 0.0, cos( age * 3.3 + phase ) ) * 7.0;
		vec3 drift = vec3( COLOR.r, 0.0, COLOR.b );
		VELOCITY = ( vec3( 0.0, rise, 0.0 ) + wobble + drift ) * IN;
		float grow = clamp( t * 8.0, 0.0, 1.0 );
		float pop = t > 0.88 ? 1.0 - ( t - 0.88 ) / 0.12 : 1.0;
		float s = max( CUSTOM.x * ( 0.6 + 0.4 * grow ) * pop * 2.0 * IN, 0.00001 );
		TRANSFORM[0].xyz = vec3( s, 0.0, 0.0 );
		TRANSFORM[1].xyz = vec3( 0.0, s, 0.0 );
		TRANSFORM[2].xyz = vec3( 0.0, 0.0, s );
	}
}
