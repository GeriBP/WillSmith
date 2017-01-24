using System.Collections;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;

public class GameStarter : MonoBehaviour
{
    private Animator animator;

    void Awake()
    {
        animator = GetComponent<Animator>();

        Assert.IsNotNull(animator);
    }

    void Update()
    {
        if (Input.GetKey(KeyCode.Escape)) //Exit Game
        {
            Application.Quit();
        }
    }

    public void BeginGame()
    {
        animator.SetTrigger("BeginGame");
        StartCoroutine(LoadNextScene());
    }

    private IEnumerator LoadNextScene()
    {
        yield return new WaitForSeconds(3f);

        SceneManager.LoadScene("scene1");
    }
}