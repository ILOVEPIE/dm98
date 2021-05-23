using Sandbox;
using Sandbox.Internal.JsonConvert;
using Sandbox.Rcon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox
{
	public class QuakePlayer : WalkController
	{

		public static readonly SoundEvent JumpSounds = new( "sounds/Q2/Player/jump/jump1.vsnd" )
		{
			Pitch = 1f,
			PitchRandom = 0f,
			UI = false,
			Volume = 0.25f,
			DistanceMax = 4000f,
		};

		float DeAccelRate { get; set; } = 10.0f;
		float AirDeAccelRate { get; set; } = 10.0f;

		bool _wishjump;

		float WaterFriction { get; set; } = 1.0f;

		public QuakePlayer()
		{
			Duck = new Duck( this );
			Unstuck = new Unstuck( this );

			GroundFriction = 6;
			AirAcceleration = 2f;
			AirControl = .0f;
			WalkSpeed = 7;
			AutoJump = true;
		}









		/// <summary>
		/// This is temporary, get the hull size for the player's collision
		/// </summary>
		public override BBox GetHull()
		{
			var girth = BodyGirth * 0.5f;
			var mins = new Vector3( -girth, -girth, 0 );
			var maxs = new Vector3( +girth, +girth, BodyHeight );

			return new BBox( mins, maxs );
		}

		public override void SetBBox( Vector3 mins, Vector3 maxs )
		{
			if ( this.mins == mins && this.maxs == maxs )
				return;

			this.mins = mins;
			this.maxs = maxs;
		}


		public override void FrameSimulate()
		{
			base.FrameSimulate();

			EyeRot = Input.Rotation;
		}

		public override void Simulate()
		{
			Entity ground = GroundEntity;
			
			_wishjump = ground == GroundEntity && Input.Down( InputButton.Jump );

			base.Simulate();
			if(ground != GroundEntity && GroundEntity == null && Input.Down(InputButton.Jump))
			{
				//Pawn.PlaySound(JumpSounds.Name);
			}
		}

		public override void WalkMove()
		{
			
			var wishdir = WishVelocity.Normal;
			var wishspeed = wishdir.Length * WishVelocity.Length;
			
			
			ApplyFriction();	
			Accelerate( wishdir, wishspeed, Acceleration);
			Velocity += BaseVelocity;

			try
			{
				if ( Velocity.Length < 1.0f )
				{
					Velocity = Vector3.Zero;
					return;
				}

				// first try just moving to the destination	
				var dest = (Position + Velocity * Time.Delta).WithZ( Position.z );

				var pm = TraceBBox( Position, dest );

				if ( pm.Fraction == 1 )
				{
					Position = pm.EndPos;
					StayOnGround();
					return;
				}

				StepMove();
			}
			finally
			{

				// Now pull the base velocity back out.   Base velocity is set if you are on a moving object, like a conveyor (or maybe another monster?)
				Velocity -= BaseVelocity;
			}

			StayOnGround();
		}

		/// <summary>
		/// Add our wish direction and speed onto our velocity
		/// </summary>
		public void Accelerate( Vector3 wishdir, float wishspeed, float acceleration )
		{
			// See if we are changing direction a bit
			var currentspeed = Velocity.Dot( wishdir );

			// Reduce wishspeed by the amount of veer.
			var addspeed = wishspeed - currentspeed;

			if ( addspeed <= 0 )
			{
				return;
			}

			// Determine amount of acceleration.
			var accelspeed = acceleration * Time.Delta * wishspeed;
			
			if ( accelspeed > addspeed )
				accelspeed = addspeed;

			Velocity += wishdir * accelspeed;
		}

		/// <summary>
		/// Remove ground friction from velocity
		/// </summary>
		public override void ApplyFriction( float frictionAmount = 1.0f )
		{

			// If we are in water jump cycle, don't apply friction
			//if ( player->m_flWaterJumpTime )
			//   return;

			// Not on ground - no friction
			float speed, newspeed, control;
			float drop;

			if(GroundEntity != null)
			{
				Velocity = new Vector3(Velocity.x, Velocity.y, 0);
			}

			speed = Velocity.Length;

			if(speed < 1)
			{
				Velocity = Velocity.WithZ(0);
				return;
			}

			drop = 0;

			if(Pawn.WaterLevel.Fraction <= 1 && GroundEntity != null)
			{
				// Bleed off some speed, but if we have less than the bleed
				//  threshold, bleed the threshold amount.
				control = (speed < StopSpeed) ? StopSpeed : speed;

				// Add the amount to the drop amount.
				drop = control * Time.Delta * frictionAmount;
			}
            
			if ( Pawn.WaterLevel.Fraction > 0 ) {
				drop += speed*WaterFriction*Pawn.WaterLevel.Fraction*Time.Delta;
			}

			// scale the velocity
			newspeed = speed - drop;
			if ( newspeed < 0 ) newspeed = 0;

			if ( newspeed != speed )
			{
				newspeed /= speed;
			}
			Velocity *= newspeed;

		}

		public override void AirMove()
		{

			var wishdir = WishVelocity.Normal;
			var wishspeed = wishdir.Length;

			wishspeed *= WalkSpeed;
			wishdir = wishdir.Normal;

			Accelerate( wishdir, wishspeed, AirAcceleration );

			if (AirControl > 0)
			{
				airControl(wishdir, wishspeed);
			}

			Velocity += BaseVelocity;

			TryPlayerMove();

			Velocity -= BaseVelocity;
		}


		private void airControl( Vector3 wishdir, float wishspeed )
		{
			Vector3 playerVelocity = Velocity;

			float zspeed;
			float speed;
			float dot;
			float k;

			// Can't control movement if not moving forward or backward
			if ( Math.Abs( WishVelocity.Normal.z ) < 0.001 || Math.Abs( wishspeed ) < 0.001 )
				return;

			zspeed = playerVelocity.z;
			playerVelocity.z = 0;


			speed = playerVelocity.Length;
			playerVelocity = playerVelocity.Normal;

			dot = Vector3.Dot( playerVelocity, wishdir );
			k = 32;

			k *= AirControl * dot * dot * Time.Delta;


			// Change direction while slowing down
			if ( dot > 0 )
			{
				playerVelocity.x = playerVelocity.x * speed + wishdir.x * k;
				playerVelocity.y = playerVelocity.y * speed + wishdir.y * k;
				playerVelocity.z = playerVelocity.z * speed + wishdir.z * k;

				playerVelocity = playerVelocity.Normal;

			}

			playerVelocity.x *= speed;
			playerVelocity.y *= speed;
			playerVelocity.z = zspeed; // Note this line

			Velocity = playerVelocity;

		}


		private void StepMove()
		{
			var startPos = Position;
			var startVel = Velocity;

			//
			// First try walking straight to where they want to go.
			//
			TryPlayerMove();

			//
			// mv now contains where they ended up if they tried to walk straight there.
			// Save those results for use later.
			//	
			var withoutStepPos = Position;
			var withoutStepVel = Velocity;

			//
			// Try again, this time step up and move across
			//
			Position = startPos;
			Velocity = startVel;
			var trace = TraceBBox( Position, Position + Vector3.Up * (StepSize + DistEpsilon) );
			if ( !trace.StartedSolid ) Position = trace.EndPos;
			TryPlayerMove();

			//
			// If we move down from here, did we land on ground?
			//
			trace = TraceBBox( Position, Position + Vector3.Down * (StepSize + DistEpsilon * 2) );
			if ( !trace.Hit || Vector3.GetAngle( Vector3.Up, trace.Normal ) > GroundAngle )
			{
				// didn't step on ground, so just use the original attempt without stepping
				Position = withoutStepPos;
				Velocity = withoutStepVel;
				return;
			}


			if ( !trace.StartedSolid )
				Position = trace.EndPos;

			var withStepPos = Position;

			float withoutStep = (withoutStepPos - startPos).WithZ( 0 ).Length;
			float withStep = (withStepPos - startPos).WithZ( 0 ).Length;

			//
			// We went further without the step, so lets use that
			//
			if ( withoutStep > withStep )
			{
				Position = withoutStepPos;
				Velocity = withoutStepVel;
				return;
			}
		}

		public override void WaterMove()
		{
			var wishdir = WishVelocity.Normal;
			var wishspeed = WishVelocity.Length;

			wishspeed *= 0.8f;
			Accelerate( wishdir, wishspeed, 100, Acceleration );

			Velocity += BaseVelocity;

			TryPlayerMove();

			Velocity -= BaseVelocity;
		}



	}
}

