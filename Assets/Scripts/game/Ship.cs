using UnityEngine;
using System.Collections;
using SpaceGame;

public class Ship : MonoBehaviour {

	public float maxSpeed;
	public float minSpeed;
	public float startSpeed;
	public float rollSpeed;
	public float yawSpeed;
	public float pitchSpeed;
	public float throttleRate;
	public float descelerateRate;
	public float tilt;
	public float speedDeadZone;
	public float shieldRegenWait = 2;
	public float shieldRegenRate = 0.5f;
	
	public float health = 1;
	public float shield = 1;
	
	public Transform cam;
	public Transform ship;
	public Transform reticule;
	public Transform deathExplosion;
	public Transform tagRoot;
	
	public float aimReticuleSpeed; // In Degrees
	public float aimRadius;

	public Transform laser;
	public int fireRate;
	public float laserVelocity;

	public GuiManager3D guiManager;
	public HealthBar healthBar;
	public string debug_msg = "";

	private Vector3 camPos;
	private float speed;
	private float lastFireTime;
	private bool alive = true;
	private float respawnTime = -1;
	private Vector3 rawRot = Vector3.zero; // Used for network approximation
	private float lastHitTime = 0;
	
	public Player player;

	// Use this for initialization
	void Start () {
		speed = startSpeed;
		camPos = cam.localPosition;
		Screen.lockCursor = true;
		player = NetVars.getPlayer(networkView.owner);

		foreach(Renderer o in reticule.gameObject.GetComponentsInChildren<Renderer>())
		{
			o.material.renderQueue = 4000; //4000+ is overlay
		}
		
		if(!IsMine ())
		{
			Destroy (reticule.gameObject);
			Destroy (cam.gameObject);
			Destroy (healthBar.gameObject);
		} else {
			//Position health bar
			Ray ray = Camera.main.ScreenPointToRay(new Vector3(100,Screen.height-100,0));
			Plane plane = new Plane(transform.forward,healthBar.gameObject.transform.position);
			
			float d = 0;
			if(plane.Raycast(ray,out d))
				healthBar.gameObject.transform.position = ray.GetPoint(d);
				
			GameObjectUtils.SetLayerRecursively(gameObject,9); // Ignore Reticle
		}
	}
	
	// Update is called once per frame
	void Update () {
		if(!alive || !IsMine())
		{
			if(alive)
			{
				transform.Rotate(Vector3.up,rawRot.y,Space.Self);
				transform.Rotate(Vector3.right,rawRot.x,Space.Self);
				transform.Rotate(Vector3.forward,rawRot.z,Space.Self);
				
				transform.Translate(Vector3.forward*trueSpeed()*Time.deltaTime,Space.Self); // Approximation for high ping
			}
			
			return;
		}
		
		if(health <= 0) {
			Kill();
			return;
		}
		
		if(shield < 1 && Time.time-lastHitTime > shieldRegenWait)
		{
			shield = Mathf.Min(1,shield+Time.deltaTime*shieldRegenRate);
			healthBar.setShield(shield);
		}
		
		float pitchRaw = Input.GetAxis("Pitch");
		float yawRaw = Input.GetAxis("Yaw");
		float rollRaw = Input.GetAxis("Roll");
		float pitch	= pitchRaw	*pitchSpeed	*Time.deltaTime;
		float yaw	= yawRaw	*yawSpeed	*Time.deltaTime;
		float roll	= rollRaw	*rollSpeed	*Time.deltaTime;
		
		rawRot = new Vector3(pitch,yaw,roll);

		transform.Rotate(Vector3.up,yaw,Space.Self);
		transform.Rotate(Vector3.right,pitch,Space.Self);
		transform.Rotate(Vector3.forward,roll,Space.Self);

		float throttle = Input.GetAxis("Throttle")*Time.deltaTime;
		if(throttle > 0) throttle *= throttleRate;
		else if(throttle < 0) throttle *= descelerateRate;
		speed = Mathf.Max(Mathf.Min(speed+throttle,maxSpeed),minSpeed);
		
		transform.Translate(Vector3.forward*trueSpeed()*Time.deltaTime,Space.Self);

		float mousex = Input.GetAxis("Mouse X");
		float mousey = Input.GetAxis("Mouse Y");
		
		float joyx = Input.GetAxis("Joystick Aim X");
		float joyy = Input.GetAxis("Joystick Aim Y");

		reticule.localPosition += new Vector3(mousex,mousey,0)*aimReticuleSpeed;
		Vector3 joyLocPos = reticule.localPosition;
		if(joyx != 0)
			joyLocPos.x = joyx*aimRadius;
		if(joyy != 0)
			joyLocPos.y = joyy*aimRadius;
		reticule.localPosition = joyLocPos;
		float xpos = reticule.localPosition.x;
		float ypos = reticule.localPosition.y;
		
		if(xpos*xpos+ypos*ypos > aimRadius*aimRadius)
		{
			float angle = Mathf.Atan2(ypos,xpos);
			xpos = Mathf.Cos(angle)*aimRadius;
			ypos = Mathf.Sin(angle)*aimRadius;
		}
		
		reticule.localPosition = new Vector3(xpos,ypos,reticule.localPosition.z);
		
		if(Input.GetButton("Fire1") && Time.time-lastFireTime > ((float)fireRate)/1000f) {
			lastFireTime = Time.time;
			
			Ray ray = new Ray(cam.position,reticule.position-cam.position);
			RaycastHit hit = new RaycastHit();
			Vector3 aim;
			int mask = ~((1 << 9) | (1 << 2));
			
			if(Physics.Raycast(ray, out hit, Mathf.Infinity, mask)) {
				aim = hit.point;
			} else aim = ray.GetPoint(1000);
			if(NetVars.Authority())
				Shoot (aim);
			else
				networkView.RPC("Shoot",RPCMode.Server,aim);
		}
			

		cam.localPosition = camPos;
		Vector3 retDir = Vector3.Normalize(new Vector3(reticule.localPosition.x,reticule.localPosition.y,3))*tilt;
		cam.localPosition += retDir;
	}

	void OnGUI() {
		if(!IsMine() && (Camera.main != null && GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(Camera.main),tagRoot.GetComponent<Collider>().bounds))) {
			Vector3 tagPos = Camera.main.WorldToScreenPoint(tagRoot.position);
			GUIStyle centered = new GUIStyle(GUI.skin.label);
			centered.alignment = TextAnchor.MiddleCenter;
			GUI.Label(new Rect(tagPos.x-100,(Screen.height-tagPos.y)-60, 200, 20), player.nickname, centered);
		}
		
		if(!IsMine ())
			return;
			
		if(!alive) {
			if(Screen.lockCursor) Screen.lockCursor = false;
			if(Time.time < respawnTime) {
				GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
				labelStyle.alignment = TextAnchor.MiddleCenter;
				GUI.Label(new Rect(0,0,Screen.width,Screen.height),("Respawn in "+(int)(respawnTime-Time.time+1)),labelStyle);
			} else if(GUI.Button(new Rect(Screen.width/2-100,Screen.height/2-50,200,100),"RESPAWN")) {
				Network.Destroy(gameObject);
				Spawner.Respawn(player);
			}
			return;
		} else if(!Screen.lockCursor) Screen.lockCursor = true;

		GUI.Label(new Rect(0,0,100,50),("Speed: "+trueSpeed()));
		//GUI.Label(new Rect(0,50,100,50),("Health: "+(health*100)));
		float sides = 32;
		Vector2? lastPoint = null;
		for(float x=0;x<=360;x+=360f/sides)
		{
			Vector3 point = transform.TransformPoint(new Vector3(aimRadius*Mathf.Cos(Mathf.Deg2Rad*x),aimRadius*Mathf.Sin(Mathf.Deg2Rad*x),reticule.localPosition.z));
			Vector2 scrPoint = Camera.main.WorldToScreenPoint(point);
			scrPoint.y = Screen.height-scrPoint.y;
			if(lastPoint == null)
			{
				lastPoint = scrPoint;
				continue;
			}
			Drawing.DrawLine(lastPoint.Value,scrPoint,new Color(1,1,1,0.5f),2,false);
			lastPoint = scrPoint;
		}
		
		GUI.Label(new Rect(Screen.width-100,0,100,500),debug_msg);
	}

	void OnTriggerEnter(Collider other) {
		if(!NetVars.Authority())
			return;
		if(other.gameObject.CompareTag("Obstacle") && alive)
		{
			if(IsMine())
				Kill();
			else {
				//alive = false;
				networkView.RPC("Kill",networkView.owner);
			}
		}
	}
	
	void OnSerializeNetworkView(BitStream stream, NetworkMessageInfo info) {
		if(stream.isWriting) {
			bool r_alive = alive;
			stream.Serialize(ref r_alive);
			
			float r_speed = speed;
			stream.Serialize(ref r_speed);
						
			Vector3 r_rot = rawRot;
			stream.Serialize(ref r_rot);
			
			NetworkPlayer r_player = player.UnityPlayer;
			stream.Serialize(ref r_player);
		} else if(stream.isReading) {
			bool r_alive = false;
			stream.Serialize(ref r_alive);
			alive = r_alive;
			
			float r_speed = 0;
			stream.Serialize(ref r_speed);
			speed = r_speed;
			
			Vector3 r_rot = Vector3.zero;
			stream.Serialize(ref r_rot);
			rawRot = r_rot;
			
			NetworkPlayer r_player = new NetworkPlayer();
			stream.Serialize(ref r_player);
			if(player == null || !player.UnityPlayer.Equals(r_player))
				player = NetVars.getPlayer(r_player);
		}
	}

	float trueSpeed()
	{
		if(speed < speedDeadZone && speed > -speedDeadZone)
			return 0;
		return speed;
	}
	
	public bool IsMine() {
		return networkView.isMine;
	}
	
	[RPC]
	void DisplayMessage(string message) {
		guiManager.displayMessage(message);
	}
	
	[RPC]
	void Kill() {
		if(!alive) return;
		alive = false;
		respawnTime = Time.time+5;
			
		Transform explosion;
		Vector3 shipPos = ship.position;
		explosion = Network.Instantiate(deathExplosion,shipPos,Quaternion.identity,0) as Transform;
		Network.Destroy(ship.gameObject);
		Destroy(reticule.gameObject);
		Destroy(healthBar.gameObject);
		explosion.position = ship.position;
		
		foreach(Collider c in GetComponents<Collider>())
			Destroy(c);
		
		if(IsMine ())
			DisplayMessage("You Died");
		else
			networkView.RPC("DisplayMessage",networkView.owner,"You Died");
	}
	
	[RPC]
	void Shoot(Vector3 aim) {
		Transform proj;
		proj = Network.Instantiate(laser, ship.position, Quaternion.identity, 0) as Transform;
		
		proj.LookAt(aim);
		proj.gameObject.GetComponent<Laser>().friendlyPlayer = player;
		proj.gameObject.GetComponent<Laser>().velocity = laserVelocity;
	}
	
	[RPC]
	public void Damage(float percent) {
		shield -= percent;
		if(shield < 0)
		{
			health += shield;
			shield = 0;
		}
		
		healthBar.setHealth(health);
		healthBar.setShield(shield);
		
		lastHitTime = Time.time;
	}
}