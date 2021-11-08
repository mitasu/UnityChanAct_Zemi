using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public sealed class KurokumaCharacterController : MonoBehaviour
{

	[SerializeField]
	bool isActive = false;
	[SerializeField]
	bool inputEnabled = true;

	[Header("必要なコンポーネントを登録")]
	[SerializeField]
	Rigidbody rigidBody = null;
	[SerializeField]
	Animator animator = null;
	[SerializeField]
	Transform groundCheckStartTransform = null;

	[Header("移動設定")]
	[SerializeField, Min(0)]
	float moveForce = 5;
	[SerializeField, Min(0)]
	float runForceMultiplier = 1.5f;
	[SerializeField]
	float turnSpeed = 180;
	[SerializeField, Min(0)]
	float jumpForce = 7;
	[SerializeField]
	float addedGravity = 3;

	[Header("接地判定の設定")]
	[SerializeField]
	float groundCheckRadius = 0.45f;
	[SerializeField]
	float groundCheckDistance = 0.1f;
	[SerializeField]
	LayerMask groundLayers = 0;

	[Header("イベントの設定")]
	[SerializeField]
	UnityEvent onCharacterEnable = null;
	[SerializeField]
	UnityEvent onCharacterDisable = null;

	//アニメーターのパラメーターのハッシュ値を取得（最適化のため）
	readonly int groundedParamHash = Animator.StringToHash("Grounded");
	readonly int forwardParamHash = Animator.StringToHash("Forward");
	readonly int jumpParamHash = Animator.StringToHash("Jump");

	//コルーチンの待ち時間のキャッシュ（最適化のため）
	readonly WaitForSeconds groundCheckWait = new WaitForSeconds(0.1f);
	readonly WaitForSeconds jumpWait = new WaitForSeconds(0.1f);

	bool isActivePrev = false;
	bool grounded = false;
	bool groundCheckEnabled = true;
	bool runInput = false;
	bool jumpInput = false;
	bool jumping = false;
	bool jumpEnabled = true;
	float forwardInput = 0;
	float turnInput = 0;
	Vector3 groundNormal = Vector3.up;
	RaycastHit hitInfo;
	Transform thisTransform;

	public bool IsActive
	{
		set
		{
			if(value != isActivePrev)
			{
				if (value)
				{
					OnCharacterEnable();
				}
				else
				{
					OnCharacterDisable();
				}
			}

			isActive = value;
		}
		get
		{
			return isActive;
		}
	}

	public bool InputEnabled
	{
		set
		{
			inputEnabled = value;
		}
		get
		{
			return inputEnabled;
		}
	}

	void Start()
	{
		thisTransform = transform;
		isActivePrev = isActive;
	}

	void Update()
	{
		if (!isActive)
		{
			return;
		}

		GetInput();
		grounded = CheckGroundStatus();
		UpdateAnimator();
	}

	void FixedUpdate()
	{
		if (!isActive)
		{
			return;
		}

		Move();
	}

	// 入力取得
	void GetInput()
	{
		if (!inputEnabled)
		{
			return;
		}

		forwardInput = Input.GetAxis("Vertical");
		turnInput = Input.GetAxis("Horizontal");
		jumpInput = Input.GetButton("Jump");
		runInput = Input.GetButton("Fire3");
	}

	// 移動
	void Move()
	{
		if (grounded)
		{
			jumping = false;

			float tempForce = moveForce;

			if (runInput && forwardInput > 0)
			{
				tempForce *= runForceMultiplier;
			}

			// 旋回
			thisTransform.Rotate(0, turnInput * turnSpeed * Time.deltaTime, 0);

			// 移動力のベクトルを算出
			Vector3 velocity = thisTransform.forward * forwardInput * tempForce;

			// 壁ずり（※坂道などに沿って移動）
			velocity = Vector3.ProjectOnPlane(velocity, groundNormal);

			// AddForceで物理移動
			Vector3 force;

			if (rigidBody.velocity.y <= 0)
			{
				//平らな場所での移動や下り坂での処理。引くベクトルのYを0にしないと、ジャンプしたときにゆっくり着地するような感じになってしまう
				force = (velocity - new Vector3(rigidBody.velocity.x, 0, rigidBody.velocity.z)) * rigidBody.mass;
			}
			else
			{
				//坂道を上るときなど。Y方向の速度が＋になると跳ねてしまうのでこうしている
				force = (velocity - rigidBody.velocity) * rigidBody.mass;
			}

			rigidBody.AddForce(force, ForceMode.Impulse);

			Jump();
		}
		else
		{
			//空中移動
			Vector3 velocity = thisTransform.forward * forwardInput * moveForce;
			Vector3 force = (velocity - new Vector3(rigidBody.velocity.x, 0, rigidBody.velocity.z)) * rigidBody.mass;
			rigidBody.AddForce(force, ForceMode.Force);

			//追加の重力
			rigidBody.AddForce(Vector3.down * addedGravity * rigidBody.mass, ForceMode.Force);
		}
	}

	// ジャンプ
	void Jump()
	{
		if(!jumpInput || jumping || !grounded || !jumpEnabled)
		{
			return;
		}

		jumping = true;
		StartCoroutine(StopGroundCheck());
		StartCoroutine(JumpTimer());

		rigidBody.AddForce(Vector3.up * jumpForce * rigidBody.mass, ForceMode.Impulse);
	}

	public void StopCharacter()
	{
		isActive = false;
		rigidBody.velocity = new Vector3(0, rigidBody.velocity.y, 0);
		animator.SetFloat(forwardParamHash, 0);
	}

	// 接地判定
	bool CheckGroundStatus()
	{
		if (!groundCheckEnabled)
		{
			return false;
		}

		if(Physics.SphereCast(groundCheckStartTransform.position, groundCheckRadius, Vector3.down, out hitInfo, groundCheckDistance, groundLayers, QueryTriggerInteraction.Ignore))
		{
			groundNormal = hitInfo.normal;
			return true;
		}
		else
		{
			return false;
		}
	}

	// ジャンプした瞬間に接地判定を一瞬止めるためのコルーチン
	IEnumerator StopGroundCheck()
	{
		if (!groundCheckEnabled)
		{
			yield break;
		}

		groundCheckEnabled = false;

		yield return groundCheckWait;

		groundCheckEnabled = true;
	}

	// ごく短時間のうちに続けてジャンプしないようにするためのコルーチン
	IEnumerator JumpTimer()
	{
		if (!jumpEnabled)
		{
			yield break;
		}

		jumpEnabled = false;

		yield return jumpWait;

		jumpEnabled = true;
	}

	// アニメーター更新
	void UpdateAnimator()
	{
		animator.SetFloat(forwardParamHash, forwardInput);
		animator.SetBool(groundedParamHash, grounded);
		animator.SetBool(jumpParamHash, jumping);

		if (runInput && forwardInput > 0)
		{
			animator.speed = runForceMultiplier;
		}
		else
		{
			animator.speed = 1;
		}
	}

	void OnCharacterEnable()
	{
		onCharacterEnable.Invoke();
	}

	void OnCharacterDisable()
	{
		onCharacterDisable.Invoke();
	}

}
